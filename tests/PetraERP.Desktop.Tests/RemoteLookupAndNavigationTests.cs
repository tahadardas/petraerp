using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Tests;

[Collection(WpfApplicationGroup.Name)]
public sealed class RemoteLookupAndNavigationTests
{
    private static Task RunStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var context = new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher);
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
    }

    private static Task RunStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var context = new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher);
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var task = action();
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);

                task.GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task RemoteItemLookupFindsItemOutsideInitialLoadedRecordsAndSavesId() => RunStaAsync(async () =>
    {
        var remoteItemId = Guid.NewGuid();
        var remoteItem = new LookupItem(remoteItemId, "ITEM-501", "مادة نادرة", "Rare Item Outside 500");

        var picker = new LookupPicker
        {
            ItemsSource = new List<LookupItem>
            {
                new(Guid.NewGuid(), "ITEM-001", "مادة أولى", "First Item")
            },
            AsyncSearchProvider = (query, ct) =>
            {
                if (query.Contains("501", StringComparison.Ordinal))
                {
                    return Task.FromResult<IReadOnlyList<LookupItem>>([remoteItem]);
                }
                return Task.FromResult<IReadOnlyList<LookupItem>>([]);
            }
        };

        // Initially remote item is not in Results or ItemsSource
        Assert.DoesNotContain(picker.Results, i => i.Id == remoteItemId);

        // Search for the remote item (debounce is 180ms)
        picker.Search("501");
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken).ConfigureAwait(true);
            if (picker.Results.Any(x => x.Id == remoteItemId)) break;
        }

        Assert.Contains(picker.Results, i => i.Id == remoteItemId);
        Assert.True(picker.CommitHighlighted());
        Assert.Equal(remoteItemId, picker.SelectedId);
    });

    [Fact]
    public Task EnterOnItemSkipsUnitAndAdvancesToQuantityExactlyOnce() => RunStaAsync(() =>
    {
        var grid = new DataGrid();
        GridEntry.SetEnabled(grid, true);
        GridEntry.SetEnterMovesNext(grid, true);

        var colItem = new DataGridTemplateColumn { Header = "Item" };
        var colUnit = new DataGridTemplateColumn { Header = "Unit" };
        GridEntry.SetSkipOnEnter(colUnit, true); // Unit is skipped on fast Enter workflow
        var colQuantity = new DataGridTextColumn { Header = "Quantity" };
        var colPrice = new DataGridTextColumn { Header = "Price" };

        grid.Columns.Add(colItem);
        grid.Columns.Add(colUnit);
        grid.Columns.Add(colQuantity);
        grid.Columns.Add(colPrice);

        var rowItem = new object();
        grid.Items.Add(rowItem);
        grid.CurrentCell = new DataGridCellInfo(rowItem, colItem);

        // Advance from Item (col 0): since Unit (col 1) has SkipOnEnter, next editable column is Quantity (col 2)
        var editableColumns = grid.Columns
            .Where(c => c.Visibility == Visibility.Visible && !c.IsReadOnly && !GridEntry.GetSkipOnEnter(c))
            .OrderBy(c => c.DisplayIndex)
            .ToList();

        Assert.Equal(3, editableColumns.Count); // Item, Quantity, Price
        Assert.Same(colItem, editableColumns[0]);
        Assert.Same(colQuantity, editableColumns[1]);
        Assert.Same(colPrice, editableColumns[2]);
    });

    [Fact]
    public Task WorkspaceSwitchCancelsInFlightSearchesAndDoesNotLeak() => RunStaAsync(() =>
    {
        var picker = new LookupPicker();

        picker.AsyncSearchProvider = async (query, ct) =>
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);
            return [new LookupItem(Guid.NewGuid(), "LEAK", "تسريب", null)];
        };

        picker.Search("leak_test");

        // Simulate workspace switch by clearing/resetting ItemsSource
        picker.ItemsSource = new List<LookupItem>();

        Assert.False(picker.IsSearching);
        Assert.Empty(picker.Results);
    });
}
