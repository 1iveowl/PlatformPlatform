---
paths: blazor/**/*.razor,blazor/**/Components/Lists/*.cs,blazor/**/wwwroot/js/data-list.js
description: Rules for list pages in Blazor on the shared DataList wrapper, its URL state model, keyboard model, delegates and cache
---

# Lists

Every list in the Blazor edition renders through `DataList<TItem>` in `blazor/Blazor.Client/Components/Lists/`. The wrapper owns QuickGrid, server pages of 25 rows, the list state in the URL, selection, row activation, the row menu column, the localized paginator, the keyboard model in `data-list.js` and the server-page cache. A feature supplies a list source, the columns and the delegates; it never touches QuickGrid.

## Implementation

1. Render `<DataList TItem="..." .../>` for a list and nothing else: `DataList.razor` is the only file that renders `QuickGrid`, and no other file imports `Microsoft.AspNetCore.Components.QuickGrid`. The component library's data grid is not used; it writes style attributes the policy blocks.
2. Declare a static `<Feature>ListSource` beside the component with the list id, the URL filter parameter names, the sort keys and default sort, `CacheScope(bootstrap)` from the tenant and user id, `NormalizeFilters` (malformed values dropped, canonical form kept), `ToQuery` (the API request) and `FetchAsync` through the typed client returning `DataListFetchResult<TItem>`. Unit-test it in `blazor/Blazor.Tests/Client/Lists/`.
3. Keep the list state in the URL and only there: the wrapper writes `orderBy`, `sortOrder`, `pageOffset` and the `SelectedKeyParameter`, and carries the page's `FilterParameters` without interpreting them; defaults are left out. A sort or page change adds a history entry; a filter, search or activation change replaces the current one. QuickGrid's `sort`, `direction` and `page` parameters are never written and are dropped whenever the wrapper writes the URL. Give a second list on one page its own `ParameterPrefix`.
4. Pass plain delegates and method groups as parameters: `FetchPage="FetchPageAsync"` (a `DataListFetch<TItem>` that receives `DataListRequest` and a `CancellationToken`), `KeySelector="KeyOf"`, `OnActivated="OpenProfileAsync"`, `OnStateChanged="OnListStateChangedAsync"`. The wrapper's own QuickGrid items provider is synchronous over the page it has already loaded; a feature never supplies a QuickGrid `ItemsProvider`, `Pagination` or a `SortBy` column.
5. Leave keys to `data-list.js`: it attaches listeners to the list root with `addEventListener`, handles arrows, Home, End, Space, Shift and Ctrl selection, Enter and Escape, and ignores keys from inputs, buttons, links and menus. Never put `@onkeydown` on the list, a row or another large surface; it would render the list on every key press. Set `EscapeScopeId` to the element whose Escape should close the activated row, and `FocusFallbackId` to a stable control (a search box) that takes focus when the activated row is closed while it is not on the loaded page.
6. Call `InvalidateAsync()` on the list reference after every mutation (role change, delete); it drops every cached page of the list id, clears the selection and loads the current page again. Change filters through `SetFiltersAsync(...)` and close the activated row through `CloseAsync()`, never by writing the URL.
7. Rely on the cache's rules rather than adding another: one `DataListPageCache` per application scope, keyed by list id, filters, sort, page size and offset; at most 40 pages; only successful pages stored; a new identity scope clears it. Personalized rows are never stored outside the runtime's memory.
8. Declare columns as `DataListColumn<TItem>(Title, Cell, SortKey, Class)` with titles from the resources, put row actions in the `RowMenu` fragment as a `DataListRowMenu` with one `DataListRowMenuItem` per action (a disabled item stays visible; the component library's menu writes style attributes), and use `EmptyContent` only to replace the default empty text.

## Examples

### Example 1 - A Feature List on the Wrapper

```razor
@* ✅ DO: the source supplies names and normalization, the component supplies delegates and fragments
   (blazor/Blazor.Client/Users/UsersSurface.razor) *@
<DataList @ref="_list" TItem="UserDetails" data-testid="users-grid" ListId="@UsersListSource.ListId" CacheScope="@UsersListSource.CacheScope(_bootstrap)"
          Label="@CommonStrings.Users" Columns="_columns" FetchPage="FetchPageAsync" KeySelector="KeyOf" DefaultOrderBy="@UsersListSource.DefaultOrderBy"
          SelectionMode="DataListSelectionMode.Multiple" SelectedKeyParameter="@UsersListSource.SelectedKeyParameter"
          FilterParameters="UsersListSource.FilterParameters" NormalizeFilters="UsersListSource.NormalizeFilters" EscapeScopeId="@PaneId"
          FocusFallbackId="@SearchId" RowLabel="RowLabel" RowMenu="RowMenu" OnActivated="OpenProfileAsync" OnClosed="OnProfileClosedAsync"
          OnMultipleSelected="CloseProfileAsync" OnStateChanged="OnListStateChangedAsync">
    <EmptyContent>
        <p data-testid="empty-state">@UsersStrings.NoUsersFound</p>
    </EmptyContent>
</DataList>

@code {

    private Task<DataListFetchResult<UserDetails>> FetchPageAsync(DataListRequest request, CancellationToken cancellationToken)
    {
        return UsersListSource.FetchAsync(Services.GetRequiredService<UsersClient>(), request, cancellationToken);
    }

}

@* ❌ DON'T: QuickGrid in a feature component; its sort and paginator write their own URL parameters and it bypasses the cache *@
<QuickGrid ItemsProvider="_provider" Pagination="_pagination">
    <PropertyColumn Property="user => user.Email" Sortable="true"/>
</QuickGrid>

@* ❌ DON'T: a keydown handler on a row; every key press renders the whole list *@
<tr @onkeydown="OnRowKeyDown">
```

### Example 2 - The Source Owns the Query

```csharp
// ✅ DO: the fetch through the typed client, the failure classified once (blazor/Blazor.Client/Users/UsersListSource.cs)
public static async Task<DataListFetchResult<UserDetails>> FetchAsync(UsersClient usersClient, DataListRequest request, CancellationToken cancellationToken)
{
    var result = await usersClient.GetUsersAsync(ToQuery(request), cancellationToken);
    if (result.IsSuccess) return DataListFetchResult<UserDetails>.Success(result.Value.Users, result.Value.TotalCount);
    var failure = ApiFailureClassifier.Classify(result);
    return DataListFetchResult<UserDetails>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
}

// ✅ DO: invalidate after a mutation (blazor/Blazor.Client/Users/UsersSurface.razor)
_isRoleDialogOpen = false;
if (_list is not null) await _list.InvalidateAsync();

// ❌ DON'T: refresh by writing the URL or by re-fetching in the component; the cache would still serve the old page
Navigation.NavigateTo(Navigation.Uri, true);
```
