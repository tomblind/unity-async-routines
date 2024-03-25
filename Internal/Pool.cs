﻿//MIT License
//
//Copyright (c) 2018 Tom Blind
//
//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:
//
//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.
//
//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

using System.Collections.Generic;
using System;

namespace AsyncRoutines.Internal
{
	public class Pool<T> where T : class, new()
	{
		private readonly Stack<T> pool = new Stack<T>();
		private int liveCount = 0;

		public T Get()
		{
			++liveCount;
			return (pool.Count > 0) ? (pool.Pop() as T) : new T();
		}

		public void Release(T obj)
		{
			--liveCount;
			pool.Push(obj);
		}

		public void Clear()
		{
			pool.Clear();
		}

		public string Report() { return string.Format("{0}/{1}", liveCount, pool.Count); }
	}

	public class TypedPool<I>
	{
		private readonly Dictionary<Type, Stack<I>> pools = new Dictionary<Type, Stack<I>>();
		private int liveCount = 0;

		public T Get<T>() where T : class, I, new()
		{
			++liveCount;
			var pool = GetPool(typeof(T));
			return (pool.Count > 0) ? (pool.Pop() as T) : new T();
		}

		public void Release(I obj)
		{
			--liveCount;
			var pool = GetPool(obj.GetType());
			pool.Push(obj);
		}

		public void Clear()
		{
			pools.Clear();
		}

		public string Report()
		{
			var c = 0;
			foreach (var t in pools.Values)
			{
				c += t.Count;
			}
			return string.Format("{0}/{1}", liveCount, c);
		}

		private Stack<I> GetPool(Type type)
		{
			if (!pools.ContainsKey(type))
			{
				var pool = new Stack<I>();
				pools[type] = pool;
				return pool;
			}
			return pools[type];
		}
	}
}
