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
	internal abstract class ResumerBase
	{
		private UInt64 id = 0;
		private Context context = null;

		public bool WasResumed { get; private set; }

		public void Setup(Context context)
		{
			this.id = context.Id;
			this.context = context;
		}

		public virtual void Reset()
		{
			id = 0;
			context = null;
			WasResumed = false;
		}

		protected Context GetContext()
		{
			if (WasResumed)
			{
				throw new Exception("Attempted to re-resume an async resumer. You must call Reset() to use a resumer again.");
			}

			else if (context == null)
			{
				WasResumed = true;
				return null;
			}

			else if (context.Id != id)
			{
				Reset();
				return null;
			}

			var _context = context;
			context = null;
			id = 0;
			WasResumed = true;
			return _context;
		}
	}

	internal class Resumer : ResumerBase, IResumer
	{
		public void Resume()
		{
			var context = GetContext();
			if (context != null)
			{
				context.Resume();
			}
		}
	}

	internal class Resumer<T> : ResumerBase, IResumer<T>
	{
		public T Result { get; private set; }

		public void Resume(T result)
		{
			Result = result;

			var context = GetContext();
			if (context != null)
			{
				context.Resume(result);
			}
		}

		public override void Reset()
		{
			Result = default(T);
			base.Reset();
		}
	}
}
