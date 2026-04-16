# Customers Maintenance UI Stylesheet

This document captures the current UI styling used by `MaintenanceNew/CustomersMaintenance`.

## 1) Page + Layout Context

- **View:** `Views/MaintenanceNew/CustomersMaintenance.cshtml`
- **Grid Partial:** `Views/MaintenanceNew/_MaintenanceCustomersGrid.cshtml`
- **Layout:** `Views/Shared/_LayoutMaintBills.cshtml`
- **Global stylesheet:** `wwwroot/css/SGStyleSheet.css`
- **Frameworks/Libraries in use:**
  - Bootstrap `4.5.2`
  - Font Awesome `5.0.13` (solid + fontawesome bundles)
  - jQuery `3.6.x/3.7.x`
  - X.PagedList pager styling classes (`pagination`, `page-item`, `page-link`)

## 2) Typography

- **Base font family:** Browser/Bootstrap default stack (no custom family is explicitly set in this page).
- **Page base size:** `0.9375rem` (`.cm-detail-page`)
- **Line height:** `1.45` (`.cm-detail-page`)
- **Card title (`Customers`):**
  - Size: `1rem`
  - Weight: `600`
  - Color: `#0f172a`
- **Section labels (`PROJECT`, `BLOCK`, `BT NO / PLOT NO`):**
  - Bootstrap classes: `font-weight-bold`, `text-secondary`, `small`, `text-uppercase`
- **Table header text:**
  - Size: `0.75rem`
  - Weight: `600`
  - Uppercase with letter-spacing `0.04em`
  - Color: `#475569`

## 3) Color Theme (Current)

### 3.1 Content area (CustomersMaintenance page-specific CSS vars)

- `--cm-surface`: `#ffffff`
- `--cm-heading`: `#0f172a`
- `--cm-border`: `#e2e8f0`
- `--cm-row-odd`: `#fafbfc`
- `--cm-row-even`: `#ffffff`
- `--cm-row-hover`: `#eef6ff`
- Primary body text in content: `#334155`

### 3.2 Card + section visuals

- Card background: `#ffffff`
- Card border: `1px solid #e2e8f0`
- Card radius: `10px`
- Card shadow: `0 2px 6px rgba(15, 23, 42, 0.06)`
- Card header gradient: `linear-gradient(180deg, #f8fafc 0%, #f1f5f9 100%)`

### 3.3 Badge

- `.cm-badge-soft`:
  - Background: `#e8f1ff`
  - Text: `#1e40af`
  - Size: `0.7rem`
  - Radius: `4px`

### 3.4 Table + pager

- Table header background: `#f1f5f9`
- Header text: `#475569`
- Cell borders: `#e2e8f0`
- Zebra row background: `#fafbfc`
- Hover background: `#eef6ff`
- Pager container background: `#f8fafc`

### 3.5 Layout shell colors (from `_LayoutMaintBills` and `SGStyleSheet.css`)

- Top nav main bar (`.navbar1-top`): deep navy theme (`#00203F` in global CSS)
- Second bar (`.navbar2-top`): white/light (`#f8f9fa` override in layout)
- Accent strip (`.navbar3-top`): `#DAA03D`
- Sidebar background (`.sidebar`): deep navy (`#00203F`)
- Sidebar link/icon accent color: yellow-green style (`yellowgreen` inline icon color)

## 4) Components

## 4.1 Filter Panel

- Container class: `.cm-filter-body`
- Input and select controls:
  - Border radius `6px`
  - Font size `0.875rem`
- Fields:
  - Project dropdown (`#projectFilter`)
  - Block dropdown (`#blockFilter`)
  - BT/Plot search textbox (`#btnoSearch`)
- Action button:
  - `#btnSearch`
  - Classes: `btn btn-primary btn-block`
  - Includes search icon (`fa-search`)

## 4.2 Data Grid

- Wrapper: `.cm-table-wrap`
  - Radius `6px`
  - Border `1px solid #e2e8f0`
- Table class: `.cm-detail-table.cm-customers-grid-table`
- Columns:
  - BT No
  - Customer Name
  - Plot No
  - Block
  - Category
  - Size
  - Project
- Empty state text:
  - "No records found."
  - Styled with `text-center text-muted py-4`
- Row interaction:
  - Cursor pointer
  - Double-click navigates to customer detail page

## 4.3 Pagination

- Uses X.PagedList with first/prev/next/last links enabled
- Bootstrap paging classes:
  - `pagination`
  - `page-item`
  - `page-link`
- Wrapped by `.cm-grid-pager` with top border and light background

## 5) Icons (Font Awesome currently used)

### 5.1 Page header + summary

- `fa-users` for title and total customers

### 5.2 Search/filter area

- `fa-search` on Search button

### 5.3 Grid header icons

- BT No: `fa-barcode`
- Customer Name: `fa-user`
- Plot No: `fa-home`
- Block: `fa-th-large`
- Category: `fa-tags`
- Size: `fa-ruler-combined`
- Project: `fa-building`

### 5.4 Maintenance sidebar (layout)

- Home: `fa-chart-line`
- Customers: `fa-user-cog`
- All Bills: `fa-search`
- Generate Bills: `fa-users`
- Print Bill: `fa-print`
- Payment Status: `fa-tasks`
- Other Links: `fa-money-bill`, caret `fa-chevron-down/up`
- Settings: `fa-cog`
- Operator Setup: `fa-id-badge`
- Tariff Rates: `fa-cog`
- Additional Charges: `fa-plus-circle`
- Reports: `fa-chart-pie`

## 6) Buttons

- **Primary action button (Search):**
  - `btn btn-primary btn-block`
  - Radius inherited from `.cm-filter-body .btn` (`6px`)
  - Weight `500`
- **Logout button (layout top bar):**
  - `btn btn-link text-black`
- **General button system:** Bootstrap 4 variants with custom radius/weight tweaks in filter area.

## 7) Spacing + Radius + Elevation Tokens

- Card radius: `10px`
- Table wrapper radius: `6px`
- Inputs/buttons radius: `6px`
- Badge radius: `4px`
- Card shadow: `0 2px 6px rgba(15, 23, 42, 0.06)`
- Filter body padding: `1rem 1.25rem`
- Pager padding: `0.75rem 1rem 1rem`

## 8) Behavior Notes

- Project change triggers async block reload.
- Search state is persisted in `sessionStorage` key:
  - `maintCustomerSearchState`
- Grid loads through AJAX partial rendering.
- Total customer counter updates from hidden grid meta (`#maintCustomerTotalMeta`).

## 9) Visual Summary (Quick Reference)

- **Look and feel:** clean enterprise dashboard style
- **Primary surfaces:** white cards on light neutral backgrounds
- **Border system:** soft gray `#e2e8f0`
- **Headers:** muted blue-gray text and light gradients
- **Sidebar:** dark navy with yellow-green icon accents
- **Interaction:** subtle blue row hover (`#eef6ff`) and pointer-enabled rows

