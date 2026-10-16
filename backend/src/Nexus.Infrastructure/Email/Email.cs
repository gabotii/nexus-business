using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nexus.Application.Abstractions;
using Nexus.Infrastructure.Persistence;

namespace Nexus.Infrastructure.Email;

public enum EmailDelivery
{
    /// <summary>Real delivery through an SMTP server (the only mode allowed in Production).</summary>
    Smtp,

    /// <summary>Writes each message as a JSON file to a local folder (development and end-to-end tests).</summary>
    Pickup,
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailDelivery Delivery { get; set; } = EmailDelivery.Smtp;

    [Required]
    public string From { get; set; } = "no-reply@localhost";

    public string? SmtpHost { get; set; }

    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 587;

    public string? SmtpUsername { get; set; }

    public string? SmtpPassword { get; set; }

    /// <summary>STARTTLS. Only a local catcher (Mailpit in Docker Compose) may run without it.</summary>
    public bool SmtpEnableSsl { get; set; } = true;

    public string PickupDirectory { get; set; } = "mail-pickup";

    /// <summary>Background delivery of the outbox. Integration tests turn it off and read the outbox directly.</summary>
    public bool DispatcherEnabled { get; set; } = true;

    [Range(1, 3600)]
    public int PollIntervalSeconds { get; set; } = 5;
}

/// <summary>
/// One queued e-mail (table <c>email_outbox</c>). Written in the same transaction as the change that triggers it;
/// the body (which contains links with tokens) is erased once the message has been delivered or abandoned.
/// </summary>
public sealed class EmailMessage
{
    public const int MaxAttempts = 8;

    public Guid Id { get; set; }

    public string To { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string? BodyText { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? FailedAt { get; set; }

    public string? LastError { get; set; }

    public void MarkSent(DateTimeOffset now)
    {
        SentAt = now;
        BodyText = null;
        LastError = null;
    }

    /// <summary>Exponential backoff (1, 2, 4… minutes); gives up after <see cref="MaxAttempts"/>.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        if (Attempts >= MaxAttempts)
        {
            FailedAt = now;
            BodyText = null;
        }
        else
        {
            NextAttemptAt = now + TimeSpan.FromMinutes(Math.Pow(2, Attempts - 1));
        }
    }
}

internal sealed class EmailOutbox(AppDbContext db, TimeProvider clock) : IEmailOutbox
{
    public void Enqueue(string recipient, string subject, string textBody)
    {
        var now = clock.GetUtcNow();
        db.EmailOutbox.Add(new EmailMessage
        {
            Id = Guid.CreateVersion7(),
            To = recipient,
            Subject = subject,
            BodyText = textBody,
            CreatedAt = now,
            NextAttemptAt = now,
        });
    }
}

public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

internal sealed class SmtpEmailTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var settings = options.Value;
        using var mail = new MailMessage(new MailAddress(settings.From), new MailAddress(message.To))
        {
            Subject = message.Subject,
            Body = message.BodyText,
            IsBodyHtml = false,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            BodyTransferEncoding = TransferEncoding.QuotedPrintable,
        };

        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort) { EnableSsl = settings.SmtpEnableSsl };
        if (!string.IsNullOrEmpty(settings.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword);
        }

        await client.SendMailAsync(mail, ct);
    }
}

/// <summary>Development stand-in: <c>{pickup}/{created}-{id}.json</c> with recipient, subject and text.</summary>
internal sealed class PickupEmailTransport(IOptions<EmailOptions> options, IHostEnvironment environment) : IEmailTransport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        // Relative paths are anchored to the API's content root, not to the process working directory.
        var directory = Path.GetFullPath(options.Value.PickupDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"{message.CreatedAt:yyyyMMddHHmmssfff}-{message.Id:N}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new
        {
            message.Id,
            message.To,
            message.Subject,
            Text = message.BodyText,
            message.CreatedAt,
        }, Json), ct);
    }
}
