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

using System;

namespace AsyncRoutines.Internal
{
	internal class Resumer : IResumer
	{
		public Routine routine = null;
		public UInt64 id = 0;
		public bool WasResumed { get; private set; }

		public void Resume()
		{
			if (routine != null)
			{
				if (id == routine.Id)
				{
					var _task = routine;
					Reset();
					_task.Step();
				}
			}
			else
			{
				WasResumed = true;
			}
		}

		public void Reset()
		{
			WasResumed = false;
			routine = null;
			id = 0;
		}
	}

	internal class Resumer<T> : IResumer<T>
	{
		public Routine<T> routine = null;
		public UInt64 id = 0;
		public T result = default(T);
		public bool WasResumed { get; private set; }

		public void Resume(T result)
		{
			if (routine != null)
			{
				if (id == routine.Id)
				{
					var _task = routine;
					Reset();
					_task.SetResult(result);
					_task.Step();
				}
			}
			else
			{
				this.result = result;
				WasResumed = true;
			}
		}

		public void Reset()
		{
			WasResumed = false;
			routine = null;
			id = 0;
		}
	}
}
