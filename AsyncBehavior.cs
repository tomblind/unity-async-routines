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
using System;
using UnityEngine;

namespace AsyncRoutines
{
	public class AsyncBehavior : MonoBehaviour
	{
		private AsyncManager asyncManager = new AsyncManager();

		public AsyncManager Manager { get { return asyncManager; } }

		public virtual void Update()
		{
			asyncManager.Update();
		}

		public virtual void LateUpdate()
		{
			asyncManager.Flush();
		}

		public void OnDestroy()
		{
			asyncManager.StopAll();
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
		public AsyncManager.ContextHandle RunAsync(Func<Task> fn, Action<Exception> onStop = null)
		{
			return asyncManager.Run(fn, onStop);
		}

		/// <summary> Stops all managed async functions </summary>
		public void StopAllAsync()
		{
			asyncManager.StopAll();
		}
	}
}
