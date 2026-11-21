import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, Outlet, useRouterState } from '@tanstack/react-router'
import { denQuery } from '../api'
import { AccountMenu, useAccess } from '../access'
import { usePosition, useUniverseKey } from '../hooks'
import { CheckpointIndicator } from './CheckpointIndicator'
import { DenErrorsIndicator, ReadersErrorCount } from './DenErrorsIndicator'
import { ContainerCreatedNotice, NewContainerProvider } from './NewContainer'
import { CreatedNotice, NewEntityProvider, NewMenu } from './NewEntity'
import { PositionBar } from './PositionBar'
import { SearchBox } from './SearchBox'
import { LiveRefresh } from '../liveRefresh'
import { Sidebar } from './Sidebar'

/**
 * Every page: the den's name and the story-position bar across the top, the
 * hierarchy tree on the left, and the page on the right (#24, #26). On a
 * narrow screen the tree folds away behind a Contents button and opens over
 * the page, below the top bar: focus moves into it, and Escape closes it and
 * returns focus to the button. In a universe, the top bar's New menu makes
 * a note from a type's template (#29), and the universe's and series'
 * pages make series and books (#213).
 */
export function Layout() {
  const den = useQuery(denQuery)
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  // Edit file by path has no universe in its route; the file's own gives the tree and the position bar one, if it loaded.
  const fileUniverse = useRouterState({
    select: (s) => (s.location.pathname === '/file' ? /^universes\/([^/]+)\//.exec(String((s.location.search as { path?: unknown }).path ?? ''))?.[1] : undefined),
  })
  const routeUniverse = useUniverseKey()
  const { readers } = usePosition()
  const { canWrite } = useAccess()
  const universe = routeUniverse ?? (den.data?.universes.some((u) => u.key === fileUniverse) ? fileUniverse : undefined)
  const [treeOpen, setTreeOpen] = useState(false)
  const appRef = useRef<HTMLDivElement>(null)
  const headerRef = useRef<HTMLElement>(null)
  const sidebarRef = useRef<HTMLElement>(null)
  const toggleRef = useRef<HTMLButtonElement>(null)

  // On a narrow screen, going to a page closes the tree.
  useEffect(() => setTreeOpen(false), [pathname])

  // The top bar wraps on a narrow screen, so the tree is placed from its
  // measured height rather than a fixed one.
  useLayoutEffect(() => {
    const header = headerRef.current
    const app = appRef.current
    if (!header || !app) {
      return
    }

    const measure = () => app.style.setProperty('--header-height', `${Math.ceil(header.getBoundingClientRect().height)}px`)
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure)
    observer?.observe(header)
    window.addEventListener('resize', measure)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', measure)
    }
  }, [])

  useEffect(() => {
    if (!treeOpen) {
      return
    }

    sidebarRef.current?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setTreeOpen(false)
        toggleRef.current?.focus()
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [treeOpen])

  return (
    <NewEntityProvider universe={universe}>
      <NewContainerProvider universe={universe}>
        <div className="app" ref={appRef}>
          <header className="topbar" ref={headerRef}>
            <button
              type="button"
              ref={toggleRef}
              className="tree-toggle"
              aria-expanded={treeOpen}
              aria-controls="sidebar"
              onClick={() => setTreeOpen(!treeOpen)}
            >
              Contents
            </button>
            <Link to="/" className="den-name">
              {den.data?.name ?? 'Wolfden'}
            </Link>
            {/* The Reader's view is read-only, and this asks for every note's name to know where a new one would go. */}
            {/* A viewer's account changes nothing (#315 D6), so it has no New menu either. */}
            {universe && !readers && canWrite && <NewMenu universe={universe} />}
            <SearchBox universe={universe} />
            <PositionBar universe={universe} />
            {/* The Reader's view is read-only, and the list of problems and of what's changed name files, whatever readers know (#300). */}
            {readers ? <ReadersErrorCount /> : <DenErrorsIndicator universe={universe} />}
            {!readers && canWrite && <CheckpointIndicator />}
            <LiveRefresh />
            <AccountMenu />
          </header>
          <div className="body">
            <aside id="sidebar" className="sidebar" data-open={treeOpen} ref={sidebarRef} tabIndex={-1}>
              <Sidebar universe={universe} />
            </aside>
            <main className="page">
              <CreatedNotice />
              <ContainerCreatedNotice />
              <Outlet />
            </main>
          </div>
        </div>
      </NewContainerProvider>
    </NewEntityProvider>
  )
}
