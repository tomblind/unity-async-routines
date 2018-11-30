# Unity AsyncRoutines

A "spiritual successor" to [Unity Routines](https://github.com/tomblind/unity-routines), AsyncRoutines is a replacement for Unity's coroutines that makes use of C# 7's async functions (available in Unity 2018.3).

Notable Features Include
- A manager component that can run routines and ensure they are stopped when the GameObject is destroyed
- Hierarchical support to allow awaiting collections of routines (WaitForAll/WaitForAny)
- Built-in support for passing AsyncOperations and CustomYieldInstructions to await
- Utilizes a custom async task builder and extensive pooling to keep routines efficient and reduce garbage

There is also an extension [Unity AsyncTweens](https://github.com/tomblind/unity-async-tweens) which adds a set of tweening routines that can be used in Async Routines.

## Basic Usage
```cs
using UnityEngine;
using AsyncRoutines;

public class MyObject : MonoBehaviour
{
    public RoutineManagerBehavior routineManager;

    public void Start()
    {
        routineManager.Run(Countdown());
    }

    public async Routine Countdown()
    {
        for (var i = 10; i >= 0; --i)
        {
            Debug.Log(i);
            await Routine.WaitForSeconds(1);
        }
    }
}
```

RoutineManagerBehavior is a component which manages routines for a specific object. All routines started with Run will be shut down when the object is destroyed. Run also returns a handle which allows individual routines to be stopped manually.

Routine provides a suite of WaitFor* methods for use in 'async Routine' methods. Note that to use certain WaitFor methods, a routine must be "managed". That means it, or one of its ancestors, must have been started with RoutineManager.Run().

## Waiting on Multiple Routines
```cs
//Resumes when all sub-routines complete
public async Routine DoAllOfTheThings()
{
    await Routine.WaitForAll(DoThingOne(), DoThingTwo(), DoThingThree());
}

//Resumes when the first sub-routine completes (and shuts down the rest)
public async Routine DoAnyOfTheThings()
{
    await Routine.WaitForAny(DoThingOne(), DoThingTwo(), DoThingThree());
}

public async Routine DoThingOne() { ... }
public async Routine DoThingTwo() { ... }
public async Routine DoThingThree() { ... }
```

## Return Values
```cs
public async Routine PrintTheNumber()
{
    var theNum = await GetTheNumber();
    Debug.Log(theNum);
}

public async Routine<int> GetTheNumber()
{
    await Routine.WaitForSeconds(1);
    return 17;
}
```

```cs
public async Routine PrintAllOfTheNumbers()
{
    //numbers is an int[] containing all of the results in order
    var numbers = await Routine.WaitForAll(GetTheFirstNumber(), GetTheSecondNumber(), GetTheThirdNumber());
    foreach (var num in numbers)
    {
        Debug.Log(num);
    }
}

public async Routine PrintAnyOfTheNumbers()
{
    //num is the result of the first routine to finish
    var num = await Routine.WaitForAny(GetTheFirstNumber(), GetTheSecondNumber(), GetTheThirdNumber());
    Debug.Log(num);
}

public async Routine<int> GetTheFirstNumber()
{
    await Routine.WaitForSeconds(3);
    return 1;
}

public async Routine<int> GetTheSecondNumber()
{
    await Routine.WaitForSeconds(2);
    return 2;
}

public async Routine<int> GetTheThirdNumber()
{
    await Routine.WaitForSeconds(1);
    return 3;
}
```

## Waiting on Event/Callbacks
AsyncRoutines provides the helper type IResumer to allow for awaiting events/callbacks.
```cs
public IResumer resumer = null;

public async Routine WaitForCallback()
{
    resumer = Routine.GetResumer();
    await resumer;
    Routine.ReleaseResumer(resumer);
    resumer = null;
}

public void OnCallback()
{
    resumer.Resume();
}
```
```cs
UnityEvent unityEvent;

public async Routine WaitForUnityEvent()
{
    var resumer = Routine.GetResumer();
    unityEvent.AddListener(resumer.Resume);
    await resumer;
    Routine.ReleaseResumer(resumer);
}
```
```cs
event Action<string> strEvent;

public async Routine WaitForEventWithString()
{
    var resumer = Routine.GetResumer<string>();
    strEvent += resumer.Resume;
    var result = await resumer;
    Routine.ReleaseResumer(resumer);
    Debug.Log(result);
}
```
Notice that IResumers are pooled and should be released when not needed. However, they can be re-used multiple times without being released.

IResumers are also "smart" about being called before being awaited upon.
```cs
public async Routine DoTheThing()
{
    var resumer = Routine.GetResumer();
    StartTheThing(resumer.Resume); //Could call resumer.Resume immediately
    await resumer; //Detects that resumer was already called and doesn't wait
    Routine.ReleaseResumer(resumer);
}

public void StartTheThing(Action finishCallback)
{
    finishCallback(); //Finishes immediately
}
```
In this example, resumer.Resume() gets called before being awaited on. In this case it's 'marked' as resumed and the await statement will resume immediately.

## Cleanup and Error Handling
Run() takes an optional onStop callback, which is always called when a routine ends, regardless of how it ended. This is a good place to do any cleanup. It is passed an Exception as its only argument. If the routine threw an unhandled exception, it will be received there. Otherwise it will be null. If onStop is not set and an exception occurs, it will be reported using Unity's Debug.LogException.

Call stacks in exceptions from async routines are not very useful. To help with this, set Routine.EnableTracing to true. This will add additional info to the exception to help trace where it came from. But, there's a small performance hit for using this, so it is off by default.

## Using Routines Outside of Behaviours
RoutineManagerBehavior is a simple wrapper around a RoutineManager object. If you want to manage your own routines without using a component, you can use RoutineManager to do so, but you must call Update(), Flush() and StopAll() yourself at appropriate times. Flush() should be called after all Updates (usually in LateUpdate).

## Notes
- Be careful not to await a Routine from a standard 'async Task' function (unless that was awaited from a routine higher up). The routine won't be associated with a manager and certain WaitFor functions will not work.
- Routines are not thread-safe. You can await on multi-threaded tasks from a routine, but do not use routines in more than one thread at once.
- Routines, resumers and the underlying state machines are all pooled. This means a sudden burst of usage of many routines can cause memory usage to increase permenantly. You can use Routine.ClearPools() to dump pooled objects at strategic times (like between scene loads) if this becomes problematic.
