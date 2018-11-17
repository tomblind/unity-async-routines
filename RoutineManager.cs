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
using System;
using UnityEngine;

namespace AsyncRoutines
{
	public class RoutineManager
	{
		private List<IResumer> nextFrameResumers = new List<IResumer>();
		private List<IResumer> pendingNextFrameResumers = new List<IResumer>();
		private readonly List<Routine> roots = new List<Routine>();

		/// <summary> Resumers managed routines that are waiting for next frame. </summary>
		public void Update()
		{
			foreach (var resumer in nextFrameResumers)
			{
				resumer.Resume();
				Routine.ReleaseResumer(resumer);
			}
			nextFrameResumers.Clear();

			for (var i = 0; i < roots.Count;)
			{
				var root = roots[i];
				if (root.IsDead)
				{
					roots.RemoveAt(i);
					Routine.Release(root);
				}
				else
				{
					++i;
				}
			}
		}

		/// <summary> Prepares next-frame resumers. Should be called in LateUpdate. </summary>
		public void Flush()
		{
			var temp = nextFrameResumers;
			nextFrameResumers = pendingNextFrameResumers;
			pendingNextFrameResumers = temp;
		}

		/// <summary> Stops all managed routines. </summary>
		public void StopAll()
		{
			foreach (var root in roots)
			{
				Routine.Release(root);
			}
			roots.Clear();
		}

		/// <summary> Manages and runs a routine. </summary>
		public RoutineHandle Run(Routine task, Action<Exception> onStop = null)
		{
			task.SetManager(this, onStop ?? DefaultOnStop);
			roots.Add(task);
			task.Step();
			return new RoutineHandle(task);
		}

		/// <summary>
		/// Schedules a resumer to be called next frame. Resumer will be auto-released after being called.
		/// </summary>
		public void AddNextFrameResumer(IResumer resumer)
		{
			pendingNextFrameResumers.Add(resumer);
		}

		private static void DefaultOnStop(Exception exception)
		{
			if (exception != null)
			{
				Debug.LogException(exception);
			}
		}
	}
}
