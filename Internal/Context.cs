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
using System.Linq;
using System;
using UnityEngine;
using UnityEngine.Assertions;

namespace AsyncRoutines.Internal
{
	internal interface IResumeManager
	{
		void AddUpdate(IResumer resumer);
	}

	internal class Context : IContext
	{
		private IYieldTaskBase yieldTask = null;
		private IYieldTaskBase steppingTask = null;
		private Action<Exception> onStop = null;
		private UInt64 id = 0;
		private bool isStepping = false;
		private Context parent = null;
		private int index = -1;
		private readonly List<Context> children = new List<Context>();
		private bool stopOnAny = false;
		private IResumeManager resumeManager;
		private Exception exception = null;
		private System.Diagnostics.StackTrace stackTrace = null;

		private static UInt64 nextId = 1;

		private static readonly Pool<Context> pool = new Pool<Context>();
		private static readonly TypedPool<ITaskBase> taskPool = new TypedPool<ITaskBase>();
		private static readonly Stack<Context> steppingStack = new Stack<Context>();
		private static readonly ContinueTask continueTask = new ContinueTask();

		public UInt64 Id { get { return id; } }
		public bool IsAlive { get { return yieldTask != null || children.Count > 0; } }

		public static Context Current { get { return steppingStack.Peek(); } }

		public ITask Continue()
		{
			return continueTask;
		}

		public ITask WaitForNextFrame()
		{
			var resumer = Async.GetResumer() as Resumer;
			resumer.Setup(this);
			resumeManager.AddUpdate(resumer);
			return Yield(GetTask<YieldTask>());
		}

		public ITask WaitForAny(IEnumerable<Func<Task>> fns)
		{
			SetupChildren(fns);
			return ResumeChildrenAny();
		}

		public ITask WaitForAny<I>(IEnumerable<I> collection, Func<I, Task> fn)
		{
			SetupChildren(collection, fn);
			return ResumeChildrenAny();
		}

		public ITask<T> WaitForAny<T>(IEnumerable<Func<Task<T>>> fns)
		{
			SetupChildren(fns);
			return ResumeChildrenAny<T>();
		}

		public ITask<T> WaitForAny<I, T>(IEnumerable<I> collection, Func<I, Task<T>> fn)
		{
			SetupChildren(collection, fn);
			return ResumeChildrenAny<T>();
		}

		public ITask WaitForAll(IEnumerable<Func<Task>> fns)
		{
			SetupChildren(fns);
			return ResumeChildrenAll();
		}

		public ITask WaitForAll<I>(IEnumerable<I> collection, Func<I, Task> fn)
		{
			SetupChildren(collection, fn);
			return ResumeChildrenAll();
		}

		public ITask<T[]> WaitForAll<T>(IEnumerable<Func<Task<T>>> fns)
		{
			SetupChildren(fns);
			return ResumeChildrenAll<T>();
		}

		public ITask<T[]> WaitForAll<I, T>(IEnumerable<I> collection, Func<I, Task<T>> fn)
		{
			SetupChildren(collection, fn);
			return ResumeChildrenAll<T>();
		}

		public ITask WaitForResumer(IResumer resumer)
		{
			var _resumer = resumer as Resumer;
			if (_resumer.WasResumed)
			{
				return continueTask;
			}
			_resumer.Setup(this);
			return Yield(GetTask<YieldTask>());
		}

		public ITask<T> WaitForResumer<T>(IResumer<T> resumer)
		{
			var _resumer = resumer as Resumer<T>;
			if (_resumer.WasResumed)
			{
				var contTask = GetTask<ContinueTask<T>>();
				contTask.result = _resumer.Result;
				return contTask;
			}
			_resumer.Setup(this);
			return Yield(GetTask<YieldTask<T>>());
		}

		public void Setup(Func<Task> fn, Action<Exception> onStop, IResumeManager resumeManager)
		{
			var startTask = GetTask<StartTask>();
			startTask.ContinueFn = fn;
			Setup(startTask, onStop, resumeManager);
		}

		public void Setup(Func<Task> fn, Action<Exception> onStop, Context parent)
		{
			var startTask = GetTask<StartTask>();
			startTask.ContinueFn = fn;
			Setup(startTask, onStop, parent.resumeManager, parent);
		}

		public void Setup<T>(Func<Task<T>> fn, Action<Exception> onStop, Context parent)
		{
			var startTask = GetTask<StartTask<T>>();
			startTask.ContinueFn = fn;
			Setup(startTask, onStop, parent.resumeManager, parent);
		}

		public void Setup<A>(Func<A, Task> fn, A a, Action<Exception> onStop, Context parent)
		{
			var startTask = GetTask<StartArgTask<A>>();
			startTask.ContinueFn = fn;
			startTask.arg = a;
			Setup(startTask, onStop, parent.resumeManager, parent);
		}

		public void SetParentResult<T>(T result)
		{
			parent.SetResult(result, index);
		}

		public void Resume<T>(T awaitResult)
		{
			SetResult(awaitResult);
			Resume();
		}

		public void Resume()
		{
			//Already resuming?
			if (isStepping)
			{
				return;
			}

			//Don't resume if children aren't ready
			if (!stopOnAny && !children.All(CheckFinished))
			{
				return;
			}
			stopOnAny = false;
			Release(children);

			//Continue the current yielding task
			isStepping = true;

			steppingTask = yieldTask;
			yieldTask = null;

			var currentId = id;
			steppingStack.Push(this);
			steppingTask.Continue();
			Assert.IsTrue(this == steppingStack.Peek());
			steppingStack.Pop();

			ReleaseTask(steppingTask);
			steppingTask = null;

			//Suicide?
			if (currentId != id)
			{
				return;
			}

			isStepping = false;

			//Finish up if no more tasks yielding
			CheckForFinish();
		}

		public void Throw(Exception e)
		{
			var root = this;

			if (Async.TracingEnabled)
			{
				var culled = new List<string>();
				CullStackTrace(e.StackTrace, culled);
				if (stackTrace != null)
				{
					culled.Add("  --From Async Context Started At--");
					CullStackTrace(stackTrace.ToString(), culled);
				}

				while (root.parent != null)
				{
					root = root.parent;
					if (stackTrace != null && root.stackTrace != null)
					{
						culled.Add("  --From Async Context Started At--");
						CullStackTrace(root.stackTrace.ToString(), culled);
					}
				}

				var msg = string.Format("{0}\n----Async Stack----\n{1}\n----End Async Stack----", e.Message, string.Join("\n", culled));
				root.exception = new Exception(msg, e);
			}
			else
			{
				while (root.parent != null)
				{
					root = root.parent;
				}
				root.exception = e;
			}

			root.Stop();
		}

		public void Stop()
		{
			Release(children);
			stopOnAny = false;

			ReleaseTask(yieldTask);
			yieldTask = null;

			id = 0;
			isStepping = false;
			parent = null;
			resumeManager = null;
			index = -1;

			if (onStop != null)
			{
				onStop(exception);
				onStop = null;
			}
			exception = null;
			stackTrace = null;
		}

		public static Context Get()
		{
			return pool.Get();
		}

		public static void Release(Context context)
		{
			context.Stop();
			pool.Release(context);
		}

		public static void Release(List<Context> contexts)
		{
			foreach (var context in contexts)
			{
				Release(context);
			}
			contexts.Clear();
		}

		public static bool CheckFinished(Context context)
		{
			return !context.IsAlive;
		}

		public static T GetTask<T>() where T : class, ITaskBase, new()
		{
			return taskPool.Get<T>();
		}

		public static void ReleaseTask(ITaskBase task)
		{
			if (task == null)
			{
				return;
			}
			task.Reset();
			taskPool.Release(task);
		}

		private T Yield<T>(T task) where T : IYieldTaskBase
		{
			Assert.IsNull(yieldTask);
			yieldTask = task;
			return task;
		}

		private void CancelYield()
		{
			Assert.IsNotNull(yieldTask);
			ReleaseTask(yieldTask);
			yieldTask = null;
		}

		private void CheckForFinish()
		{
			//Not done yet?
			if (yieldTask != null)
			{
				return;
			}

			//Step parent when finished
			var _parent = parent;
			Stop();
			if (_parent != null)
			{
				_parent.Resume();
			}
		}

		private void SetResult<T>(T result)
		{
			Assert.IsTrue(yieldTask is IYieldTask<T>);
			(yieldTask as IYieldTask<T>).SetResult(result);
		}

		private void SetResult<T>(T result, int childIndex)
		{
			if (yieldTask is YieldTask<T[]>)
			{
				(yieldTask as YieldTask<T[]>).result[childIndex] = result;
			}
			else
			{
				SetResult(result);
			}
		}

		private void Setup(IYieldTaskBase startTask, Action<Exception> onStop, IResumeManager resumeManager, Context parent = null)
		{
			if (Async.TracingEnabled)
			{
				stackTrace = new System.Diagnostics.StackTrace(true);
			}

			yieldTask = startTask;

			id = nextId++;
			if (id == 0)
			{
				throw new Exception("Ran out of ids for async contexts!");
			}

			this.parent = parent;
			this.resumeManager = resumeManager;
			this.onStop = onStop;
		}

		private void SetupChildren(IEnumerable<Func<Task>> fns)
		{
			var i = 0;
			foreach (var fn in fns)
			{
				var context = Get();
				context.Setup(fn, null, this);
				context.index = i++;
				children.Add(context);
			}
		}

		private void SetupChildren<T>(IEnumerable<Func<Task<T>>> fns)
		{
			var i = 0;
			foreach (var fn in fns)
			{
				var context = Get();
				context.Setup(fn, null, this);
				context.index = i++;
				children.Add(context);
			}
		}

		private void SetupChildren<I>(IEnumerable<I> collection, Func<I, Task> fn)
		{
			var i = 0;
			foreach (var item in collection)
			{
				var context = Get();
				context.Setup(fn, item, null, this);
				context.index = i++;
				children.Add(context);
			}
		}

		private void SetupChildren<I, T>(IEnumerable<I> collection, Func<I, Task<T>> fn)
		{
			var i = 0;
			foreach (var item in collection)
			{
				var context = Get();
				context.Setup(fn, item, null, this);
				context.index = i++;
				children.Add(context);
			}
		}

		private ITask ResumeChildrenAny()
		{
			if (children.Count == 0)
			{
				return continueTask;
			}

			var currentId = id;
			var anyTask = Yield(GetTask<YieldTask>());
			foreach (var context in children)
			{
				context.Resume();
				if (id != currentId)
				{
					return continueTask;
				}
				if (!context.IsAlive)
				{
					CancelYield();
					Release(children);
					return continueTask;
				}
			}
			stopOnAny = true;
			return anyTask;
		}

		private ITask<T> ResumeChildrenAny<T>()
		{
			if (children.Count == 0)
			{
				return GetTask<ContinueTask<T>>();
			}

			var currentId = id;
			var anyTask = Yield(GetTask<YieldTask<T>>());
			foreach (var context in children)
			{
				context.Resume();
				if (id != currentId)
				{
					return GetTask<ContinueTask<T>>();
				}
				if (!context.IsAlive)
				{
					var contTask = GetTask<ContinueTask<T>>();
					contTask.result = anyTask.result;
					CancelYield();
					Release(children);
					return contTask;
				}
			}
			stopOnAny = true;
			return anyTask;
		}

		private ITask ResumeChildrenAll()
		{
			if (children.Count == 0)
			{
				return continueTask;
			}

			var currentId = id;
			var allTask = Yield(GetTask<YieldTask>());
			foreach (var context in children)
			{
				context.Resume();
				if (currentId != id)
				{
					return continueTask;
				}
			}
			if (children.All(CheckFinished))
			{
				CancelYield();
				Release(children);
				return continueTask;
			}
			return allTask;
		}

		private ITask<T[]> ResumeChildrenAll<T>()
		{
			if (children.Count == 0)
			{
				return GetTask<ContinueTask<T[]>>();
			}

			var currentId = id;
			var allTask = Yield(GetTask<YieldTask<T[]>>());
			allTask.result = new T[children.Count];
			foreach (var context in children)
			{
				context.Resume();
				if (currentId != id)
				{
					return GetTask<ContinueTask<T[]>>();
				}
			}
			if (children.All(CheckFinished))
			{
				var contTask = GetTask<ContinueTask<T[]>>();
				contTask.result = allTask.result;
				CancelYield();
				Release(children);
				return contTask;
			}
			return allTask;
		}

		private static void CullStackTrace(string stackTrace, List<string> culled)
		{
			foreach (var _line in stackTrace.Split("\r\n".ToCharArray()))
			{
				var line = _line.Trim();
				if (
					line.StartsWith("at ")
					&& !line.StartsWith("at System.")
					&& !line.StartsWith("at AsyncRoutines.")
					&& !line.EndsWith(">:0")
				)
				{
					culled.Add(line);
				}
			}
		}
	}
}
