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

using AsyncRoutines.Internal;

namespace AsyncRoutines
{
	public static class Async
	{
		private static Pool<Resumer> resumerPool = new Pool<Resumer>();
		private static TypedPool<IResumerBase> resumerArgPool = new TypedPool<IResumerBase>();
		public static IContext Context { get { return Internal.Context.Current; } }
		public static bool TracingEnabled { get; set; }

		public static IResumer GetResumer()
		{
			return resumerPool.Get();
		}

		public static IResumer<T> GetResumer<T>()
		{
			return resumerArgPool.Get<Resumer<T>>();
		}

		public static void ReleaseResumer(IResumer resumer)
		{
			var _resumer = resumer as Resumer;
			_resumer.Reset();
			resumerPool.Release(_resumer);
		}

		public static void ReleaseResumer<T>(IResumer<T> resumer)
		{
			var _resumer = resumer as Resumer<T>;
			_resumer.Reset();
			resumerArgPool.Release(_resumer);
		}
	}
}
