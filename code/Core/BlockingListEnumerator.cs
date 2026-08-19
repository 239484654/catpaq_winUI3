// Copyright (c) Files Community. Licensed under the MIT License.
// 照抄 Files 4.2.3 (src\Files.App\Utils\Storage\Collection\BlockingListEnumerator.cs)
namespace Catpaq.Core;

internal sealed class BlockingListEnumerator<T> : IEnumerator<T>
{
	private readonly List<T> _list;
	private readonly object _syncRoot;
	private int _index = -1;

	public BlockingListEnumerator(List<T> list, object syncRoot)
	{
		_list = list;
		_syncRoot = syncRoot;
	}

	public T Current
	{
		get
		{
			lock (_syncRoot)
			{
				return _list[_index];
			}
		}
	}

	object IEnumerator.Current => Current!;

	public bool MoveNext()
	{
		lock (_syncRoot)
		{
			_index++;
			return _index < _list.Count;
		}
	}

	public void Reset()
	{
		lock (_syncRoot)
		{
			_index = -1;
		}
	}

	public void Dispose()
	{
	}
}
