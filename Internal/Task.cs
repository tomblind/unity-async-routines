//MIT License
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

using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using UnityEngine.Assertions;

namespace AsyncRoutines.Internal
{
	internal interface IYieldTaskBase : ITaskBase
	{
		void Continue();
	}

	internal interface IYieldTask : IYieldTaskBase, ITask
	{
	}

	internal interface IYieldTask<T> : IYieldTaskBase, ITask<T>
	{
		void SetResult(T result);
	}

	internal interface IStartTask<F> : IYieldTaskBase where F : Delegate
	{
		F ContinueFn { get; set; }
	}

	internal class StartTask : IStartTask<Func<Task>>, IYieldTask
	{
		public Func<Task> ContinueFn { get; set; }
		public ITask GetAwaiter() { return this; }
		public void GetResult() {}
		public bool IsCompleted { get { Assert.IsTrue(false); return false; } }
		public void OnCompleted(Action continueFn) { Assert.IsTrue(false); }
		public void Reset() { ContinueFn = null; }
		public async void Continue()
		{
			var context = Context.Current;
			try
			{
				await ContinueFn();
			}
			catch (Exception e)
			{
				context.Throw(e);
			}
		}
	}

	internal class StartTask<T> : IStartTask<Func<Task<T>>>, IYieldTask<T>
	{
		public Func<Task<T>> ContinueFn { get; set; }
		public ITask<T> GetAwaiter() { return this; }
		public T GetResult() { Assert.IsTrue(false); return default(T); }
		public bool IsCompleted { get { Assert.IsTrue(false); return false; } }
		public void OnCompleted(Action continueFn) { Assert.IsTrue(false); }
		public void Reset() { ContinueFn = null; }
		public async void Continue()
		{
			var context = Context.Current;
			var id = context.Id;
			T result;
			try
			{
				result = await ContinueFn();
			}
			catch (Exception e)
			{
				context.Throw(e);
				return;
			}
			if (context.Id != id)
			{
				return;
			}
			context.SetParentResult(result);
		}
		public void SetResult(T result) {}
	}

	internal class StartArgTask<A> : IStartTask<Func<A, Task>>, IYieldTask
	{
		public Func<A, Task> ContinueFn { get; set; }
		public A arg = default(A);
		public ITask GetAwaiter() { return this; }
		public void GetResult() {}
		public bool IsCompleted { get { Assert.IsTrue(false); return false; } }
		public void OnCompleted(Action continueFn) { Assert.IsTrue(false); }
		public void Reset() { arg = default(A); ContinueFn = null; }
		public async void Continue()
		{
			var context = Context.Current;
			try
			{
				await ContinueFn(arg);
			}
			catch (Exception e)
			{
				context.Throw(e);
			}
		}
	}

	internal class YieldTask : IYieldTask
	{
		public Action continueFn = null;
		public ITask GetAwaiter() { return this; }
		public void GetResult() {}
		public bool IsCompleted { get { return false; } }
		public void OnCompleted(Action continueFn) { this.continueFn = continueFn; }
		public void Reset() { continueFn = null; }
		public void Continue() { continueFn(); }
	}

	internal class YieldTask<T> : IYieldTask<T>
	{
		public Action continueFn = null;
		public T result = default(T);
		public ITask<T> GetAwaiter() { return this; }
		public T GetResult() { return result; }
		public bool IsCompleted { get { return false; } }
		public void OnCompleted(Action continueFn) { this.continueFn = continueFn; }
		public void Reset() { result = default(T); continueFn = null; }
		public void Continue() { continueFn(); }
		public void SetResult(T result) { this.result = result; }
	}

	internal class ContinueTask : ITask
	{
		public ITask GetAwaiter() { return this; }
		public void GetResult() {}
		public bool IsCompleted { get { return true; } }
		public void OnCompleted(Action continueFn) { Assert.IsTrue(false); }
		public void Reset() {}
	}

	internal class ContinueTask<T> : ITask<T>
	{
		public T result = default(T);
		public ITask<T> GetAwaiter() { return this; }
		public T GetResult()
		{
			var _result = result;
			Context.ReleaseTask(this); //Release self since no one else will get the chance to
			return _result;
		}
		public bool IsCompleted { get { return true; } }
		public void OnCompleted(Action continueFn) { Assert.IsTrue(false); }
		public void Reset() { result = default(T); }
	}
}
