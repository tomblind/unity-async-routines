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

namespace AsyncRoutines
{
	// ITasks simulate System.Threading.Task when passed to await.
	// There's no formal interface in C# for this (it's all compiler magic).
	//
	// When passed to await, the following occurs:
	// -GetAwaiter() is called, which should always just return the ITask, which is a valid awaiter
	// -IsCompleted is checked
	// -If IsCompleted returns true:
	//   -The async function is resumed immediately
	// -Else
	//   -OnCompleted() is called and given an action that will resume the async function when called
	// -When the async function is resumed, GetResult() is called to to get a value to return to the await expression

	public interface ITaskBase : INotifyCompletion
	{
		bool IsCompleted { get; }
		void Reset();
	}

	public interface ITask : ITaskBase
	{
		ITask GetAwaiter();
		void GetResult();
	}

	public interface ITask<T> : ITaskBase
	{
		ITask<T> GetAwaiter();
		T GetResult();
	}

	public static class ITaskExtensions
	{
		public static async Task AsTask(this ITask task)
		{
			await task;
		}

		public static async Task<T> AsTask<T>(this ITask<T> task)
		{
			return await task;
		}
	}
}
