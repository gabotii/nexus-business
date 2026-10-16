import { createContext, useContext } from 'react'
import type { Workspace } from './api'

export interface WorkspaceContextValue {
  workspace: Workspace
  /** UX only: whether to offer an action. The API enforces the permission regardless. */
  can: (permission: string) => boolean
}

export const WorkspaceContext = createContext<WorkspaceContextValue | null>(null)

/** The opened company. Only valid inside the workspace layout (`/c/:slug/*`). */
export function useWorkspace(): WorkspaceContextValue {
  const value = useContext(WorkspaceContext)
  if (!value) throw new Error('useWorkspace must be used inside the workspace layout.')
  return value
}
