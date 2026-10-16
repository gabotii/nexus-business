/**
 * Commercial name, set at build time with VITE_PRODUCT_NAME (Docker: the PRODUCT_NAME variable feeds both this and
 * the API's Branding:ProductName). Nothing else in the code base hard-codes the product name.
 */
export const PRODUCT_NAME: string = import.meta.env.VITE_PRODUCT_NAME?.trim() || 'Nexus Business'
