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
using System.Runtime.CompilerServices;
using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.Assertions;
using AsyncRoutines.Internal;
using UnityEngine.ResourceManagement.AsyncOperations;

//This needs to be explicitly defined for the compiler
namespace System.Runtime.CompilerServices
{
	public sealed class AsyncMethodBuilderAttribute : Attribute
	{
		public Type BuilderType { get; }

		public AsyncMethodBuilderAttribute(Type builderType)
		{
			BuilderType = builderType;
		}
	}
}

namespace AsyncRoutines
{
#if DEBUG_ROUTINES
	public class RoutineException : Exception
	{
		public RoutineException(string message, Exception innerException) : base(message, innerException) {}
	}
#endif

	//Routines do triple duty as task-likes, task-builders, and awaiters in order to keep internal access/pooling easy
	public abstract class RoutineBase : INotifyCompletion
	{
        public RoutineManager Manager => manager;

		/// <summary> Enable stack tracing for debugging. Off by default due to performance implications. </summary>
		public static bool TracingEnabled { get; set; }
#if DEBUG_ROUTINES
            = true;
#else
            = false;
#endif

		/// <summary> The running instance id of the routine. If the routine is stopped, will return zero. </summary>
		public UInt64 Id { get { return id; } }

		/// <summary> Indicates if routine is stopped. </summary>
		public bool IsDead { get { return id == 0; } }

		/// <summary> Internal use only. Required for awaiter. </summary>
		public bool IsCompleted { get { return state == State.Finished; } }

#if DEBUG_ROUTINES
		/// <summary> The current async/await stack trace for this routine </summary>
		public string StackTrace
		{
			get
			{
				string formatFrame(System.Diagnostics.StackFrame frame)
				{
					if (frame == null)
					{
						return "(unknown) at unknown:0:0";
					}

					var filePath = frame.GetFileName();
					if (filePath != null)
					{
						filePath = filePath.Replace("\\", "/");
						var assetsIndex = filePath.IndexOf("/Assets/");
						if (assetsIndex >= 0)
						{
							filePath = filePath.Substring(assetsIndex + 1);
						}
					}
					else
					{
						filePath = "<filename unknown>";
					}

					return string.Format(
						"{0}.{1} (at <a href=\"{2}\" line=\"{3}\">{2}:{3}:{4}</a>)",
						frame.GetMethod().DeclaringType,
						frame.GetMethod().Name,
						filePath,
						frame.GetFileLineNumber(),
						frame.GetFileColumnNumber()
					);
				}
				var routine = this;
				var stackTrace = formatFrame(routine.stackFrame);
				while (routine.parent != null)
				{
					routine = routine.parent;
					stackTrace += "\n" + formatFrame(routine.stackFrame);
				}
				return stackTrace;
			}
		}
#endif

		protected enum State
		{
			NotStarted,
			Running,
			Finished
		}

		protected interface IStateMachineRef
		{
			void MoveNext();
		}

		protected class StateMachineRef<T> : IStateMachineRef where T : IAsyncStateMachine
		{
			public T value;

            [HideInCallstack]
			public void MoveNext() { value.MoveNext(); }
		}

        public static void ResetStatics()
        {
            stateMachinePool = new TypedPool<IStateMachineRef>();
            nextId = 1;
            steppingStack = new Stack<RoutineBase>();

            pool = new TypedPool<RoutineBase>();
            resumerPool = new TypedPool<IResumerBase>();
            awaiterListPool = new Pool<List<RoutineBase>>();
        }

		protected UInt64 id = 0; //Used to verify a routine is still the same instance and hasn't been recycled
		protected State state = State.NotStarted;
		protected bool stopChildrenOnStep = false; //Kill children when stepping. Used by WaitForAny
		protected IStateMachineRef stateMachine = null; //The generated state machine for the async method
		protected RoutineManager manager = null; //The manager to use for WaitForNextFrame
		protected RoutineBase parent = null; //Routine that spawned this one
		protected readonly List<RoutineBase> children = new List<RoutineBase>(); //Routines spawned by this one
		protected Action onFinish = null; //Continuation to call when async method is finished
		protected Action<Exception> onStop = null;
		protected Exception thrownException = null;
		protected readonly Action stepAction;
		protected Action stepAnyAction;
		protected Action stepAllAction;

        protected static Exception GetThrownException(RoutineBase routine)
        {
            return routine.thrownException;
        }

#if DEBUG_ROUTINES
		protected System.Diagnostics.StackFrame stackFrame = null; //Track where the routine was created for debugging
#endif

		//Top-most routine currently being stepped
		public static RoutineBase Current { get { return (steppingStack.Count > 0) ? steppingStack.Peek() : null; } }

		//State machines are pooled by keeping a wrapped class version. This is pointless in debug where the state
		//machines are generated as classes, but useful in release where they are structs.
		protected static TypedPool<IStateMachineRef> stateMachinePool = new TypedPool<IStateMachineRef>();

		private static UInt64 nextId = 1; //Id generator. 64bits should be enough, right?

		//Tracks actively stepping routines
		private static Stack<RoutineBase> steppingStack = new Stack<RoutineBase>();

		//Pools
		private static TypedPool<RoutineBase> pool = new TypedPool<RoutineBase>();
		private static TypedPool<IResumerBase> resumerPool = new TypedPool<IResumerBase>();
		private static Pool<List<RoutineBase>> awaiterListPool = new Pool<List<RoutineBase>>();

		//Is routine active?
		private bool IsRunning { get { return !IsDead && !IsCompleted; }}

		public RoutineBase()
		{
			stepAction = Step;
		}

		public abstract object GetBoxedResult();

		/// <summary> Stop the routine. </summary>
		public void Stop()
		{
			id = 0;
			ReleaseChildren();
			stopChildrenOnStep = false;
			onFinish = null;

			if (onStop != null)
			{
				onStop(thrownException);
				onStop = null;
			}
		}

        [HideInCallstack]
		/// <summary> Internal use only. Executes to the next await or end of the async method. </summary>
		public void Step()
		{
			if (IsDead)
			{
				return;
			}

			//First step
			if (state == State.NotStarted)
			{
				state = State.Running;
			}

			//Step async method to the next await
			if (stateMachine != null)
			{
				//Stop children, but don't release them because their result might be needed
				if (stopChildrenOnStep)
				{
					foreach (var child in children)
					{
						child.Stop();
					}
				}

				var currentId = id;
				steppingStack.Push(this);
				stateMachine.MoveNext();
				Assert.IsTrue(steppingStack.Peek() == this);
				steppingStack.Pop();
				if (currentId != id)
				{
					return;
				}

				//Now we can release dead children back to the pool
				for (var i = 0; i < children.Count;)
				{
					var child = children[i];
					if (child.IsDead)
					{
						children.RemoveAt(i);
						Release(child);
					}
					else
					{
						++i;
					}
				}
			}

			//Routine was not an async method
			else
			{
				ReleaseChildren();
				state = State.Finished;
			}

			//All done: resume parent if needed
			if (state == State.Finished)
			{
				Finish();
			}
		}

		/// <summary> Internal use only. Receives continuation to call when async method finishes. </summary>
		public void OnCompleted(Action continuation)
		{
			onFinish = continuation;
		}

		/// <summary> Internal use only. Throw an exception into the routine. </summary>
		public void Throw(Exception exception)
		{
			var awaitingRoutines = awaiterListPool.Get();
			CollectAwaitingRoutines(this, awaitingRoutines);

			var currentIsAwaiting = false;
			foreach (var routine in awaitingRoutines)
			{
				if (!routine.IsRunning)
				{
					continue;
				}
				else if (routine == Current)
				{
					currentIsAwaiting = true;
				}
				else
				{
					routine.OnException(exception);
				}
			}

			awaitingRoutines.Clear();
			awaiterListPool.Release(awaitingRoutines);

			if (currentIsAwaiting && Current.IsRunning)
			{
				throw exception;
			}
		}

		/// <summary> Internal use only. Store the current stack frame for debugging. </summary>
		[System.Diagnostics.Conditional("DEBUG_ROUTINES")]
		public void Trace(int frame)
		{
#if DEBUG_ROUTINES
			if (TracingEnabled)
			{
				stackFrame = new System.Diagnostics.StackFrame(frame + 1, true);
			}
#endif
		}

		/// <summary> Dump pooled objects to clear memory. </summary>
		public static void ClearPools()
		{
			stateMachinePool.Clear();
			pool.Clear();
			resumerPool.Clear();
			awaiterListPool.Clear();
		}

		public static void Report()
		{
			UnityEngine.Debug.LogFormat("stateMachinePool = {0}", stateMachinePool.Report());
		    UnityEngine.Debug.LogFormat("pool = {0}", pool.Report());
			UnityEngine.Debug.LogFormat("resumerPool = {0}", resumerPool.Report());
			UnityEngine.Debug.LogFormat("awaiterListPool = {0}", awaiterListPool.Report());
		}

		/// <summary> Get a routine from the pool. If yield is false routine will resume immediately from await. </summary>
		public static T Get<T>(bool yield) where T : RoutineBase, new()
		{
			var current = Current;
			if (current != null && current.IsDead)
			{
				throw new Exception("Routine is dead!");
			}
			var routine = pool.Get<T>();
			routine.Setup(yield, current);
			return routine;
		}

		/// <summary> Release routine back to pool. </summary>
		public static void Release(RoutineBase routine)
		{
			routine.Reset();
			pool.Release(routine);
		}

		/// <summary> Get a resumer from the pool. </summary>
		public static IResumer GetResumer()
		{
			return resumerPool.Get<Resumer>();
		}

		/// <summary> Get a resumer from the pool. </summary>
		public static IResumer<T> GetResumer<T>()
		{
			return resumerPool.Get<Resumer<T>>();
		}

		/// <summary> Release a resumer to the pool. </summary>
		public static void ReleaseResumer(IResumer resumer)
		{
			resumer.Reset();
			resumerPool.Release(resumer);
		}

		/// <summary> Release a resumer to the pool. </summary>
		public static void ReleaseResumer<T>(IResumer<T> resumer)
		{
			resumer.Reset();
			resumerPool.Release(resumer);
		}

		/// <summary>
		/// Do-nothing routine that resumes immediately. Good for quieting warning about async functions with no await
		/// statement.
		/// </summary>
		public static Routine Continue()
		{
			var continueRoutine = Get<Routine>(false);
			continueRoutine.SetResult();
			return continueRoutine;
		}

		/// <summary>
		/// Do-nothing routine that resumes immediately with the specified result. Good for quieting warning about async
		/// functions with no await statement.
		/// </summary>
		public static Routine<T> Continue<T>(T result)
		{
			var continueRoutine = Get<Routine<T>>(false);
			continueRoutine.SetResult(result);
			return continueRoutine;
		}

		/// <summary> Routine the yields until the next frame's update. Current routine must be managed. </summary>
		public static Routine WaitForNextFrame()
		{
			var nextFrameRoutine = Get<Routine>(true);
			nextFrameRoutine.Trace(1);
			var resumer = new LightResumer{routine = nextFrameRoutine, id = nextFrameRoutine.id};
			Current.manager.AddNextFrameResumer(ref resumer);
			return nextFrameRoutine;
		}

		/// <summary> Routine that yields until all routines in a collection complete. </summary>
		public static Routine WaitForAll(IEnumerable<RoutineBase> routines)
		{
			var allRoutine = Get<Routine>(true);
			allRoutine.Trace(1);
			var isCompleted = true;
			var currentId = allRoutine.id;
			foreach (var routine in routines)
			{
				routine.SetParent(allRoutine);
				routine.Start();
				if (allRoutine.id != currentId)
				{
					Assert.IsTrue(allRoutine.IsDead);
					return allRoutine;
				}
				if (!routine.IsCompleted)
				{
					routine.OnCompleted(allRoutine.stepAllAction);
					isCompleted = false;
				}
			}
			if (isCompleted)
			{
				allRoutine.StepAll();
			}
			return allRoutine;
		}

		/// <summary> Routine that yields until all routines in a collection complete. </summary>
		public static Routine WaitForAll(params RoutineBase[] routines)
		{
			var allRoutine = WaitForAll((IEnumerable<RoutineBase>)routines);
			allRoutine.Trace(1);
			return allRoutine;
		}

		/// <summary>
		/// Routine that yields until all routines in a collection complete. Returns array of results.
		/// </summary>
		public static Routine<T[]> WaitForAll<T>(IEnumerable<Routine<T>> routines)
		{
			var allRoutine = Get<Routine<T[]>>(true);
			if (allRoutine.stepAllAction == null)
			{
				allRoutine.stepAllAction = allRoutine.StepAll<T>;
			}
			allRoutine.Trace(1);
			var isCompleted = true;
			var currentId = allRoutine.id;
			foreach (var routine in routines)
			{
				routine.SetParent(allRoutine);
				routine.Start();
				if (allRoutine.id != currentId)
				{
					Assert.IsTrue(allRoutine.IsDead);
					return allRoutine;
				}
				if (!routine.IsCompleted)
				{
					routine.OnCompleted(allRoutine.stepAllAction);
					isCompleted = false;
				}
			}
			if (isCompleted)
			{
				allRoutine.StepAll<T>();
			}
			return allRoutine;
		}

		/// <summary>
		/// Routine that yields until all routines in a collection complete. Returns array of results.
		/// </summary>
		public static Routine<T[]> WaitForAll<T>(params Routine<T>[] routines)
		{
			var allRoutine = WaitForAll((IEnumerable<Routine<T>>)routines);
			allRoutine.Trace(1);
			return allRoutine;
		}

		/// <summary>
		/// Routine that yields until the first routine in a collection completes. The others will be stopped at that
		/// time.
		/// </summary>
		public static Routine WaitForAny(IEnumerable<RoutineBase> routines)
		{
			var anyRoutine = Get<Routine>(true);
			anyRoutine.Trace(1);
			anyRoutine.stopChildrenOnStep = true;
			var isCompleted = false;
			var currentId = anyRoutine.id;
			foreach (var routine in routines)
			{
				routine.SetParent(anyRoutine);
				if (!isCompleted)
				{
					routine.Start();
					if (anyRoutine.id != currentId)
					{
						Assert.IsTrue(anyRoutine.IsDead);
						return anyRoutine;
					}
					isCompleted = routine.IsCompleted;
					if (!isCompleted)
					{
						routine.OnCompleted(anyRoutine.stepAnyAction);
					}
				}
			}
			if (anyRoutine.children.Count == 0 || isCompleted)
			{
				anyRoutine.StepAny();
			}
			return anyRoutine;
		}

		/// <summary>
		/// Routine that yields until the first routine in a collection completes. The others will be stopped at that
		/// time.
		/// </summary>
		public static Routine WaitForAny(params RoutineBase[] routines)
		{
			var anyRoutines = WaitForAny((IEnumerable<RoutineBase>)routines);
			anyRoutines.Trace(1);
			return anyRoutines;
		}

		/// <summary>
		/// Routine that yields until the first routine in a collection completes. The others will be stopped at that
		/// time. Returns result from completed routine.
		/// </summary>
		public static Routine<T> WaitForAny<T>(IEnumerable<Routine<T>> routines)
		{
			var anyRoutine = Get<Routine<T>>(true);
			anyRoutine.Trace(1);
			anyRoutine.stopChildrenOnStep = true;
			var isCompleted = false;
			var currentId = anyRoutine.id;
			foreach (var routine in routines)
			{
				routine.SetParent(anyRoutine);
				if (!isCompleted)
				{
					routine.Start();
					if (anyRoutine.id != currentId)
					{
						Assert.IsTrue(anyRoutine.IsDead);
						return anyRoutine;
					}
					isCompleted = routine.IsCompleted;
					if (!isCompleted)
					{
						routine.OnCompleted(anyRoutine.stepAnyAction);
					}
				}
			}
			if (anyRoutine.children.Count == 0 || isCompleted)
			{
				anyRoutine.StepAny();
			}
			return anyRoutine;
		}

		/// <summary>
		/// Routine that yields until the first routine in a collection completes. The others will be stopped at that
		/// time. Returns result from completed routine.
		/// </summary>
		public static Routine<T> WaitForAny<T>(params Routine<T>[] routines)
		{
			var anyRoutine = WaitForAny((IEnumerable<Routine<T>>)routines);
			anyRoutine.Trace(1);
			return anyRoutine;
		}

		/// <summary> Routine that yields for a set amount of time. Uses Unity game time. </summary>
		public static async Routine WaitForSeconds(float seconds)
		{
			var endTime = Time.time + seconds;
        	while (Time.time < endTime)
			{
				await WaitForNextFrame();
			}
		}

		/// <summary> Routine that yields until a condition has been met. </summary>
		public static async Routine WaitUntil(Func<bool> condition)
		{
			while (!condition())
			{
				await WaitForNextFrame();
			}
		}

		/// <summary>
		/// Routine that yields until a resumer is resumed.
		/// Useful for using resumers in WaitForAll/WaitForAny.
		/// </summary>
		public static async Routine WaitForResumer(IResumer resumer)
		{
			await resumer;
		}

		/// <summary>
		/// Routine that yields until a resumer is resumed.
		/// Useful for using resumers in WaitForAll/WaitForAny.
		/// </summary>
		public static async Routine<T> WaitForResumer<T>(IResumer<T> resumer)
		{
			return await resumer;
		}

		/// <summary> Routine that yields until an AsyncOperation has completed. </summary>
		public static async Routine WaitForAsyncOperation(AsyncOperation asyncOperation)
		{
			if (!asyncOperation.isDone)
			{
				var resumer = GetResumer<AsyncOperation>();
				asyncOperation.completed += resumer.Resume;
				await resumer;
				ReleaseResumer(resumer);
			}
		}

		/// <summary> Routine that yields until a CustomYieldInstruction has completed. </summary>
		public static async Routine WaitForCustomYieldInstruction(CustomYieldInstruction customYieldInstruction)
		{
			while (customYieldInstruction.keepWaiting)
			{
				await WaitForNextFrame();
			}
		}

		public void Start()
		{
			if (manager == null)
			{
				throw new Exception("Routine is not associated with a manager!");
			}
			if (state == State.NotStarted)
			{
				Step();
			}
		}

        [HideInCallstack]
		protected void Finish()
		{
			state = State.Finished;

			var _onFinish = onFinish;
			Stop();

			if (_onFinish != null)
			{
				_onFinish(); //Resume parent
			}
		}

		protected virtual void Reset()
		{
			Stop();
			state = State.NotStarted;
			if (stateMachine != null)
			{
				stateMachinePool.Release(stateMachine);
				stateMachine = null;
			}
			thrownException = null;
			parent = null;
			manager = null;
		}

        [HideInCallstack]
		protected void OnException(Exception exception)
		{
#if DEBUG_ROUTINES
			if (TracingEnabled && !(exception is RoutineException) && !(exception is AggregateException))
			{
				exception = new RoutineException(
					string.Format($"{exception.Message}\n----Async Stack----\n{StackTrace}\n---End Async Stack---\n"),
					exception
				);
			}
#endif

			thrownException = (thrownException != null)
				? new AggregateException(thrownException, exception)
				: exception;

			Finish();
		}

		protected void ThrowPendingException()
		{
			if (thrownException == null) {
				return;
			}

			var exception = thrownException;
			thrownException = null;
			ExceptionDispatchInfo.Capture(exception).Throw();
		}

		private void Setup(bool yield, RoutineBase parent)
		{
			id = nextId++;
			SetParent(parent);
			state = yield ? State.Running : State.NotStarted;
		}

		private void ReleaseChildren()
		{
			foreach (var child in children)
			{
				Release(child);
			}
			children.Clear();
		}

		protected void SetParent(RoutineBase newParent)
		{
			if (parent == newParent)
			{
				return;
			}

			if (parent != null)
			{
				Assert.IsTrue(parent.children.Contains(this));
				parent.children.Remove(this);
			}

			parent = newParent;

			if (newParent != null)
			{
				manager = parent.manager;
				newParent.children.Add(this);
			}
			else
			{
				manager = null;
			}
		}

		private static void CollectAwaitingRoutines(RoutineBase routine, List<RoutineBase> awaitingRoutines)
		{
			var isAwaiting = true;
			foreach (var child in routine.children)
			{
				if (child.IsRunning)
				{
					isAwaiting = false;
					CollectAwaitingRoutines(child, awaitingRoutines);
				}
			}
			if (isAwaiting)
			{
				awaitingRoutines.Add(routine);
			}
		}
	}

	[AsyncMethodBuilder(typeof(Routine))]
	public class Routine : RoutineBase
	{
		public Routine() : base()
		{
			stepAllAction = StepAll;
			stepAnyAction = StepAny;
		}

		/// <summary> Assign a manager and stop handler to a routine. </summary>
		public void SetManager(RoutineManager manager, Action<Exception> onStop)
		{
			SetParent(null);
			this.manager = manager;
			this.onStop = onStop;
		}

		/// <summary> Internal use only. Required for awaiter. </summary>
		public void GetResult()
		{
            if (thrownException != null) {
                ThrowPendingException();
            }
		}

		public override object GetBoxedResult()
		{
			GetResult();
			return null;
		}

		/// <summary> Internal use only. Required for task-like. </summary>
		public Routine GetAwaiter()
		{
			Start(); //Step when passed to await
			return this;
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
		{
			var stateMachineRef = stateMachinePool.Get<StateMachineRef<TStateMachine>>();
			stateMachineRef.value = stateMachine;
			this.stateMachine = stateMachineRef;
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void SetStateMachine(IAsyncStateMachine stateMachine) {}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void SetResult()
		{
			Assert.IsTrue(state != State.Finished);
			state = State.Finished;
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void SetException(Exception exception)
		{
			OnException(exception);
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
			where TAwaiter : INotifyCompletion
			where TStateMachine : IAsyncStateMachine
		{
			awaiter.OnCompleted(stepAction);
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
			where TAwaiter : ICriticalNotifyCompletion
			where TStateMachine : IAsyncStateMachine
		{
			awaiter.OnCompleted(stepAction);
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public Routine Task { get { return this; } }

		/// <summary> Internal use only. Steps a routine only if all of it's children are finished. </summary>
		public void StepAll()
		{
			foreach (var child in children)
			{
				if (!child.IsCompleted)
				{
					return;
				}
			}

			//Propagate exceptions
			foreach (var child in children)
			{
				var childException = GetThrownException(child);
				if (childException != null)
				{
					thrownException = (thrownException != null)
						? new AggregateException(thrownException, childException)
						: childException;
				}
			}
			SetResult(); //Mark as finished

			Step();
		}

		/// <summary> Internal use only. Steps a routine and sets it's result from the first completed child. </summary>
		public void StepAny()
		{
			//Propagate exception
			foreach (var child in children)
			{
				if (child.IsCompleted) {
                    var childException = GetThrownException(child);
					thrownException = childException;
					break;
				}
			}
			SetResult(); //Mark as finished

			Step();
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public static Routine Create()
		{
			var routine = Get<Routine>(false);
			routine.Trace(2);
			return routine;
		}
	}

	[AsyncMethodBuilder(typeof(Routine<>))]
	public class Routine<T> : RoutineBase
	{
		private T result = default(T);

		public Routine() : base()
		{
			stepAnyAction = StepAny;
		}

		/// <summary> Internal use only. Required for awaiter. </summary>
		public T GetResult()
		{
            if (thrownException != null) {
                ThrowPendingException();
            }
			return result;
		}

		public override object GetBoxedResult()
		{
			return GetResult();
		}

		/// <summary> Internal use only. Required for task-like. </summary>
		public Routine<T> GetAwaiter()
		{
			Start(); //Step when passed to await
			return this;
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
		{
			var stateMachineRef = stateMachinePool.Get<StateMachineRef<TStateMachine>>();
			stateMachineRef.value = stateMachine;
			this.stateMachine = stateMachineRef;
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void SetStateMachine(IAsyncStateMachine stateMachine) {}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void SetResult(T result)
		{
			this.result = result;
			Assert.IsTrue(state != State.Finished);
			state = State.Finished;
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void SetException(Exception exception)
		{
			OnException(exception);
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
			where TAwaiter : INotifyCompletion
			where TStateMachine : IAsyncStateMachine
		{
			awaiter.OnCompleted(stepAction);
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
			where TAwaiter : ICriticalNotifyCompletion
			where TStateMachine : IAsyncStateMachine
		{
			awaiter.OnCompleted(stepAction);
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public Routine<T> Task { get { return this; } }

		/// <summary>
		/// Internal use only. Steps a routine only if all of it's children are finished and sets the result array.
		/// </summary>
		public void StepAll<I>()
		{
			foreach (var child in children)
			{
				if (!child.IsCompleted)
				{
					return;
				}
			}

			//Build results array and propagate exceptions
			var resultArray = new I[children.Count];
			for (var i = 0; i < children.Count; ++i)
			{
				var child = (children[i] as Routine<I>);
				resultArray[i] = child.result;

				var childException = child.thrownException;
				if (childException != null)
				{
					thrownException = (thrownException != null)
						? new AggregateException(thrownException, childException)
						: childException;
				}
			}
			(this as Routine<I[]>).SetResult(resultArray);

			Step();
		}

		/// <summary> Internal use only. Steps a routine and sets it's result from the first completed child. </summary>
		public void StepAny()
		{
			foreach (var child in children)
			{
				//Propagate result and exception
				if (child.IsCompleted)
				{
					var _child = (child as Routine<T>);
					thrownException = _child.thrownException;
					SetResult(_child.result);
					break;
				}
			}

			Step();
		}

		/// <summary> Internal use only. Required for task builder. </summary>
		public static Routine<T> Create()
		{
			var routine = Get<Routine<T>>(false);
			routine.Trace(2);
			return routine;
		}

		protected override void Reset()
		{
			result = default(T);
			base.Reset();
		}
	}

	//Extensions to allow certain types to be awaited with using Routine.WaitFor
	public static class RoutineExtensions
	{
		public static Routine GetAwaiter(this AsyncOperation asyncOperation)
		{
			return Routine.WaitForAsyncOperation(asyncOperation).GetAwaiter();
		}

		public static Routine GetAwaiter(this CustomYieldInstruction customYieldInstruction)
		{
			return Routine.WaitForCustomYieldInstruction(customYieldInstruction).GetAwaiter();
		}

		public static Routine GetAwaiter(this IResumer resumer)
		{
			var _resumer = resumer as Resumer;
			Assert.IsNotNull(_resumer);
			var resumerRoutine = Routine.Get<Routine>(true);
			resumerRoutine.Trace(1);
			_resumer.routine = resumerRoutine;
			_resumer.id = resumerRoutine.Id;
			if (_resumer.WasResumed)
			{
				resumerRoutine.SetResult();
				_resumer.Reset();
			}
			return resumerRoutine;
		}

		public static Routine<T> GetAwaiter<T>(this IResumer<T> resumer)
		{
			var _resumer = resumer as Resumer<T>;
			Assert.IsNotNull(_resumer);
			var resumerRoutine = Routine.Get<Routine<T>>(true);
			resumerRoutine.Trace(1);
			_resumer.routine = resumerRoutine;
			_resumer.id = resumerRoutine.Id;
			if (_resumer.WasResumed)
			{
				resumerRoutine.SetResult(_resumer.result);
				_resumer.Reset();
			}
			return resumerRoutine;
		}
	}
}
