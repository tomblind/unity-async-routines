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

using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using UnityEngine;
using AsyncRoutines.Internal;

namespace AsyncRoutines
{
	public interface IContext
	{
		ITask Continue();
		ITask WaitForNextFrame();
		ITask WaitForAny(IEnumerable<Func<Task>> fns);
		ITask WaitForAny<I>(IEnumerable<I> collection, Func<I, Task> fn);
		ITask<T> WaitForAny<T>(IEnumerable<Func<Task<T>>> fns);
		ITask<T> WaitForAny<I, T>(IEnumerable<I> collection, Func<I, Task<T>> fn);
		ITask WaitForAll(IEnumerable<Func<Task>> fns);
		ITask WaitForAll<I>(IEnumerable<I> collection, Func<I, Task> fn);
		ITask<T[]> WaitForAll<T>(IEnumerable<Func<Task<T>>> fns);
		ITask<T[]> WaitForAll<I, T>(IEnumerable<I> collection, Func<I, Task<T>> fn);
		ITask WaitForResumer(IResumer resumer);
		ITask<T> WaitForResumer<T>(IResumer<T> resumer);
	}

	public static class IContextExtensions
	{
		public static ITask WaitForAny(this IContext context, params Func<Task>[] fns)
		{
			return context.WaitForAny((IEnumerable<Func<Task>>)fns);
		}

		public static ITask<R> WaitForAny<R>(this IContext context, params Func<Task<R>>[] fns)
		{
			return context.WaitForAny((IEnumerable<Func<Task<R>>>)fns);
		}

		public static ITask WaitForAll(this IContext context, params Func<Task>[] fns)
		{
			return context.WaitForAll((IEnumerable<Func<Task>>)fns);
		}

		public static ITask<R[]> WaitForAll<R>(this IContext context, params Func<Task<R>>[] fns)
		{
			return context.WaitForAll((IEnumerable<Func<Task<R>>>)fns);
		}

		public static async Task WaitForSeconds(this IContext context, float seconds)
		{
			var endTime = Time.time + seconds;
			while (Time.time < endTime)
			{
				await context.WaitForNextFrame();
			}
		}

		public static async Task WaitUntil(this IContext context, Func<bool> condition)
		{
			while (!condition())
			{
				await context.WaitForNextFrame();
			}
		}

		public static async Task WaitForAsyncOperation(this IContext context, AsyncOperation asyncOperation, Action<float> onProgress = null)
		{
			if (asyncOperation.isDone)
			{
				return;
			}
			if (onProgress != null)
			{
				var lastProgress = asyncOperation.progress;
				while (!asyncOperation.isDone)
				{
					if (asyncOperation.progress > lastProgress)
					{
						lastProgress = asyncOperation.progress;
						onProgress(lastProgress);
					}
					await context.WaitForNextFrame();
				}
			}
			else
			{
				var resumer = Async.GetResumer<AsyncOperation>();
				asyncOperation.completed += resumer.Resume;
				await context.WaitForResumer(resumer);
				Async.ReleaseResumer(resumer);
			}
		}

		public static async Task WaitForCustomYieldInstruction(this IContext context, CustomYieldInstruction customYieldInstruction)
		{
			while (customYieldInstruction.keepWaiting)
			{
				await context.WaitForNextFrame();
			}
		}
	}
}
