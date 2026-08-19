// Copyright (c) Files Community. Licensed under the MIT License.
// 照抄 Files 4.2.3 (src\Files.App\Utils\Storage\Collection\BulkConcurrentObservableCollection.cs)
// 裁剪：分组（GroupedCollection）相关功能，仅保留线程安全列表 + 批量操作 + 排序。
using System.Collections.Specialized;
using System.ComponentModel;

namespace Catpaq.Core;

public class BulkConcurrentObservableCollection<T> : INotifyCollectionChanged, INotifyPropertyChanged, ICollection<T>, IList<T>, ICollection, IList
{
	protected bool isBulkOperationStarted;
	private readonly object syncRoot = new object();
	private readonly List<T> collection = [];

	public bool IsSorted { get; set; }

	public int Count
	{
		get
		{
			lock (syncRoot)
			{
				return collection.Count;
			}
		}
	}

	public bool IsReadOnly => false;

	public bool IsFixedSize => false;

	public bool IsSynchronized => true;

	public object SyncRoot => syncRoot;

	object? IList.this[int index]
	{
		get
		{
			return this[index];
		}
		set
		{
			if (value is not null)
				this[index] = (T)value;
		}
	}

	public T this[int index]
	{
		get
		{
			lock (syncRoot)
			{
				return collection[index];
			}
		}
		set
		{
			NotifyCollectionChangedEventArgs e;
			lock (syncRoot)
			{
				var item = collection[index];
				collection[index] = value;

				e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, item, index);
				OnCollectionChanged(e, false);
			}
		}
	}

	public event NotifyCollectionChangedEventHandler? CollectionChanged;

	public event PropertyChangedEventHandler? PropertyChanged;

	private Func<T, object>? itemSortKeySelector;

	public Func<T, object>? ItemSortKeySelector
	{
		get => itemSortKeySelector;
		set => itemSortKeySelector = value;
	}

	public BulkConcurrentObservableCollection()
	{
	}

	public BulkConcurrentObservableCollection(IEnumerable<T> items)
	{
		AddRange(items);
	}

	public virtual void BeginBulkOperation()
	{
		lock (syncRoot)
		{
			isBulkOperationStarted = true;
		}
	}

	protected void OnCollectionChanged(NotifyCollectionChangedEventArgs e, bool countChanged = true)
	{
		if (!isBulkOperationStarted)
		{
			if (countChanged)
				PropertyChanged?.Invoke(this, EventArgsCache.CountPropertyChanged);

			PropertyChanged?.Invoke(this, EventArgsCache.IndexerPropertyChanged);
			CollectionChanged?.Invoke(this, e);
		}
	}

	public virtual void EndBulkOperation()
	{
		lock (syncRoot)
		{
			if (!isBulkOperationStarted)
				return;

			isBulkOperationStarted = false;
			OnCollectionChanged(EventArgsCache.ResetCollectionChanged);
		}
	}

	public void Add(T? item)
	{
		if (item is null)
			return;

		NotifyCollectionChangedEventArgs e;

		lock (syncRoot)
		{
			var count = collection.Count;
			collection.Add(item);

			e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, count);
			OnCollectionChanged(e);
		}
	}

	public void Clear()
	{
		lock (syncRoot)
		{
			collection.Clear();
			OnCollectionChanged(EventArgsCache.ResetCollectionChanged);
		}
	}

	public bool Contains(T? item)
	{
		if (item is null)
			return false;

		lock (syncRoot)
		{
			return collection.Contains(item);
		}
	}

	public void CopyTo(T[] array, int arrayIndex)
	{
		lock (syncRoot)
		{
			collection.CopyTo(array, arrayIndex);
		}
	}

	public bool Remove(T? item)
	{
		if (item is null)
			return false;

		NotifyCollectionChangedEventArgs e;

		lock (syncRoot)
		{
			var index = collection.IndexOf(item);

			if (index == -1)
				return false;

			collection.RemoveAt(index);

			e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index);
			OnCollectionChanged(e);
		}

		return true;
	}

	public IEnumerator<T> GetEnumerator()
	{
		return new BlockingListEnumerator<T>(collection, syncRoot);
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public int IndexOf(T? item)
	{
		if (item is null)
			return -1;

		lock (syncRoot)
		{
			return collection.IndexOf(item);
		}
	}

	public void Insert(int index, T? item)
	{
		if (item is null)
			return;

		NotifyCollectionChangedEventArgs e;

		lock (syncRoot)
		{
			collection.Insert(index, item);

			e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index);
			OnCollectionChanged(e);
		}
	}

	public void RemoveAt(int index)
	{
		NotifyCollectionChangedEventArgs e;

		lock (syncRoot)
		{
			var item = collection[index];
			collection.RemoveAt(index);

			e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index);
			OnCollectionChanged(e);
		}
	}

	public void AddRange(IEnumerable<T> items)
	{
		if (!items.Any())
			return;

		NotifyCollectionChangedEventArgs e;

		lock (syncRoot)
		{
			var count = collection.Count;
			collection.AddRange(items);

			e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToList(), count);
			OnCollectionChanged(e);
		}
	}

	public void InsertRange(int index, IEnumerable<T> items)
	{
		if (!items.Any())
			return;

		NotifyCollectionChangedEventArgs e;

		lock (syncRoot)
		{
			collection.InsertRange(index, items);

			e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToList(), index);
			OnCollectionChanged(e);
		}
	}

	public void RemoveRange(int index, int count)
	{
		if (count <= 0)
			return;

		NotifyCollectionChangedEventArgs e;

		lock (syncRoot)
		{
			var items = collection.Skip(index).Take(count).ToList();
			collection.RemoveRange(index, count);

			e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, items, index);
			OnCollectionChanged(e);
		}
	}

	public void ReplaceRange(int index, IEnumerable<T> items)
	{
		var count = items.Count();

		if (count == 0)
			return;

		NotifyCollectionChangedEventArgs e;

		lock (syncRoot)
		{
			var oldItems = collection.Skip(index).Take(count).ToList();
			var newItems = items.ToList();
			collection.RemoveRange(index, count);
			collection.InsertRange(index, newItems);

			e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newItems, oldItems, index);
			OnCollectionChanged(e, false);
		}
	}

	public void Sort()
	{
		lock (syncRoot)
		{
			collection.Sort();
		}
	}

	public void Sort(Comparison<T> comparison)
	{
		lock (syncRoot)
		{
			collection.Sort(comparison);
		}
	}

	public void Order(Func<List<T>, IEnumerable<T>> func)
	{
		lock (syncRoot)
		{
			ReplaceRange(0, func.Invoke(collection));
		}
	}

	public void OrderOne(Func<List<T>, IEnumerable<T>> func, T item)
	{
		lock (syncRoot)
		{
			var result = func.Invoke(collection).ToList();

			Remove(item);
			var index = result.IndexOf(item);
			if (index != -1)
				Insert(index, item);
		}
	}

	int IList.Add(object? value)
	{
		if (value is null)
			return -1;

		lock (syncRoot)
		{
			var count = collection.Count;
			var index = ((IList)collection).Add((T)value);

			var e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value, count);
			OnCollectionChanged(e);
			return index;
		}
	}

	bool IList.Contains(object? value)
	{
		return Contains((T?)value);
	}

	int IList.IndexOf(object? value)
	{
		return IndexOf((T?)value);
	}

	void IList.Insert(int index, object? value)
	{
		Insert(index, (T?)value);
	}

	void IList.Remove(object? value)
	{
		Remove((T?)value);
	}

	void ICollection.CopyTo(Array array, int index)
	{
		CopyTo((T[])array, index);
	}

	private static class EventArgsCache
	{
		internal static readonly PropertyChangedEventArgs CountPropertyChanged = new PropertyChangedEventArgs("Count");
		internal static readonly PropertyChangedEventArgs IndexerPropertyChanged = new PropertyChangedEventArgs("Item[]");
		internal static readonly NotifyCollectionChangedEventArgs ResetCollectionChanged = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
	}
}
