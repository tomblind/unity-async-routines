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

using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using UnityEngine;
using AsyncRoutines.Internal;

namespace AsyncRoutines
{
	public class AsyncManager
	{
		public struct ContextHandle
		{
			private Context context;
			private UInt64 id;

			public bool IsAlive { get { return (context != null && id == context.Id && context.IsAlive); } }

			public ContextHandle(IContext context)
			{
				this.context = context as Context;
				this.id = this.context.Id;
			}

			public void Stop()
			{
				if (id == context.Id)
				{
					context.Stop();
				}
			}
		}

		private class ResumeManager : IResumeManager
		{
			private AsyncManager asyncManager;
			public ResumeManager(AsyncManager asyncManager) { this.asyncManager = asyncManager; }
			public void AddUpdate(IResumer resumer) { asyncManager.nextUpdateResumers.Add(resumer); }
		}

		private readonly List<Context> contexts = new List<Context>();
		private List<IResumer> updateResumers = new List<IResumer>();
		private List<IResumer> nextUpdateResumers = new List<IResumer>();
		private readonly ResumeManager resumeManager;

		public AsyncManager()
		{
			resumeManager = new ResumeManager(this);
		}

		public void Update()
		{
			foreach (var resumer in updateResumers)
			{
				resumer.Resume();
				Async.ReleaseResumer(resumer);
			}
			updateResumers.Clear();
		}

		public void Flush()
		{
			var temp = nextUpdateResumers;
			nextUpdateResumers = updateResumers;
			updateResumers = temp;

			for (var i = 0; i < contexts.Count;)
			{
				var context = contexts[i];
				if (!context.IsAlive)
				{
					contexts.RemoveAt(i);
					Context.Release(context);
				}
				else
				{
					++i;
				}
			}
		}

		/// <summary>
		/// Run an async method
		/// <para />
		/// Required signature for fn:
		///     async Task MyMethod() {...}
		/// <para />
		/// Note that onStop is called when functions completes or is stopped early. Exception will be null if no error
		/// occurred.
		/// </summary>
		public ContextHandle Run(Func<Task> fn, Action<Exception> onStop = null)
		{
			var context = Context.Get();
			context.Setup(fn, (onStop != null) ? onStop : DefaultOnStop, resumeManager);
			context.Resume();
			contexts.Add(context);
			return new ContextHandle(context);
		}

		/// <summary> Stops all managed async functions </summary>
		public void StopAll()
		{
			Context.Release(contexts);
		}

		private static void DefaultOnStop(Exception e)
		{
			if (e != null)
			{
				Debug.LogException(e);
			}
		}
	}
}
