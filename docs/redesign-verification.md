# Bright design and dark mode — 15 November 2026

BookBridge now follows the selected bright, friendly concept: blue accents, a light catalogue search banner, readable sans-serif text, illustrated books, and a horizontal header. The same visual system covers lending pages, reports, account forms, and profiles. The application remains a Blazor library system with its existing data and authorization rules.

## Navigation and catalogue

| Role | Main navigation | Personal controls |
| --- | --- | --- |
| Member | Home, Books, My Shelf, Profile | Header notification bell, named profile/avatar link, theme toggle, Sign out |
| Librarian | Dashboard, Books, Lending desk, Members, Reports | Header notification bell, named profile/avatar link, theme toggle, Sign out |

**My Shelf** groups **My loans** and **Reservations**. **Lending desk** groups **Loans & returns** and **Reservations**. The existing `/loans` and `/reservations` routes remain available. **Reports** opens the overdue report. Password changes are reached from **Profile**.

On small screens, the navigation opens from the menu button. Keyboard focus moves to its first link; Escape closes it and restores focus to the button. Following a navigation link closes the menu. The header's notification control stays available independently of the navigation panel.

The Books page places search first, followed by category chips, the full category selector, an availability filter, and sorting by title or recently added. Book cards expose availability and a clear next step. Four original illustrated cover assets are included for *Cosmos*, *Clean Code*, *A Brief History of Time*, and *A Wizard of Earthsea*. Other titles use their configured cover URL or a styled text cover; an image loading failure also reveals the text fallback.

## Dark mode and lending rules

The theme toggle works on interactive pages and the statically rendered account forms. A saved light/dark selection persists through page changes and reloads. Without a saved selection, the app follows the operating system's preference. Theme initialization runs before page rendering to avoid a flash of the opposite theme; unavailable local storage does not prevent the toggle from working.

An available physical copy is still issued by a librarian. Members join a reservation queue when no copies are available. The catalogue and book details explain this distinction. Checkout, returns, pickup, FIFO allocation, the 72-hour collection window, borrowing eligibility, and the three-member-login maximum are unchanged.

## Verification completed

- Release build: **zero warnings and errors**.
- Windows unit tests: **74 passed**.
- Windows integration tests: **76 passed**, including catalogue sorting.
- Chromium using Microsoft Edge: **18 browser cases passed**, covering existing lending flows, new navigation, profile isolation, mobile layouts, and both-role theme persistence through reloads and account pages.
- Accessibility: **46 axe scans with zero reported violations**. The **38 incomplete findings** involved 167 nodes: 138 gradient contrast checks, 19 non-text/decorative glyph checks, four reservation-link overlap checks, and six empty-table header checks. Focused DOM, style, and visual review covered 24 role/theme/routes; it found no page overflow or actual reservation-link overlap. Computed gradient endpoint text contrast was at least **4.82:1**; empty tables have descriptive messages outside the table. Catalogue, detail, profile, staff loans, and reservations were readable in both themes. This combines automated and focused manual checks; it is not a screen-reader audit or proof of full WCAG conformance.

These are the observed local results for this change. Check the [GitHub Actions run](https://github.com/Birzhik228/Final_IFP/actions) for the presented commit to confirm its cross-platform and cross-browser results. Earlier dated verification reports retain their original counts and screenshots.
