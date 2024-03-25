﻿//MIT License
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
using UnityEngine;

namespace AsyncRoutines
{
	public class RoutineManagerBehavior : MonoBehaviour
	{
		public RoutineManager Manager { get { return routineManager; } }

		private RoutineManager routineManager = new RoutineManager();

		public virtual void Update()
		{
			routineManager.Update();
		}

		public virtual void LateUpdate()
		{
			routineManager.Flush();
		}

		public void OnDestroy()
		{
			routineManager.StopAll();
		}

		/// <summary> Manages and runs a routine. </summary>
		public RoutineHandle Run(Routine routine, Action<Exception> onStop = null)
		{
			return routineManager.Run(routine, onStop);
		}

		/// <summary> Stops all managed routines. </summary>
		public void StopAll()
		{
			routineManager.StopAll();
		}

		/// <summary> Throws an exception in all managed routines. </summary>
		public void ThrowAll(Exception exception)
		{
			routineManager.ThrowAll(exception);
		}
	}
}
