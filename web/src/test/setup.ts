import { configure } from '@testing-library/dom'

// Every test file runs this first (vite.config.ts, test.setupFiles).
//
// The suite runs its files in parallel, and on a busy machine (dotnet test
// at the same time, say) rendering the app can take seconds. With the
// defaults (1 s for findBy and waitFor, 5 s a test), tests failed now and
// then for no reason in the code (#207), and worse, a test that timed out
// went on running and clicked through the next test's page. So the waits
// are long: they cost nothing when the page is quick, since a wait ends as
// soon as what it waits for is there.
configure({ asyncUtilTimeout: 20_000 })
