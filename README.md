# Unity AsyncRoutines

A "spiritual successor" to [Unity Routines](https://github.com/tomblind/unity-routines), AsyncRoutines is a replacement for Unity's coroutines that makes use of C# 7's async functions (available in Unity 2018.3). It provides support for spawning 'routines' as async functions and associating them with a GameObject so they can be shut down when the object is destroyed (or manually, if needed). It also provides heirarchical support for awaiting mutliple async functions at once (WaitForAll/WaitForAny).

## Basic Usage
```cs
using UnityEngine;
using System.Threading.Tasks;
using AsyncRoutines;

public class MyObject : MonoBehaviour
{
    public AsyncBehavior asyncManager;

    public void Start()
    {
        asyncManager.RunAsync(Countdown);
    }

    public async Task Countdown()
    {
        for (var i = 10; i >= 0; --i)
        {
            Debug.Log(i);
            await Async.Context.WaitForSeconds(1);
        }
    }
}
```

AsyncBehavior is a component which manages async routines for a specific object. Async.Context provides a suite of WaitFor* methods which mirror the common ones Unity provides. Note that to use Async.Context, the routine must be started with RunAsync(), or the context will not be created yet, when accessed.

## Waiting on Multiple Routines
```cs
//Resumes when all sub-routines complete
public async Task DoAllOfTheThings()
{
    await Async.Context.WaitForAll(DoThingOne, DoThingTwo, DoThingThree);
}

//Resumes when the first sub-routine completes (and shuts down the rest)
public async Task DoAnyOfTheThings()
{
    await Async.Context.WaitForAll(DoThingOne, DoThingTwo, DoThingThree);
}

public async Task DoThingOne() { ... }
public async Task DoThingTwo() { ... }
public async Task DoThingThree() { ... }
```

## Return Values
```cs
public async Task PrintTheNumber()
{
    var theNum = await GetTheNumber();
    Debug.Log(theNum);
}

public async Task<int> GetTheNumber()
{
    await Async.Context.WaitForSeconds(1);
    return 17;
}
```

```cs
public async Task PrintAllOfTheNumbers()
{
    //numbers is an int[] containing all of the results in order
    var numbers = await Async.Context.WaitForAll(GetTheFirstNumber, GetTheSecondNumber, GetTheThirdNumber);
    foreach (var num in numbers)
    {
        Debug.Log(num);
    }
}

public async Task PrintAnyOfTheNumbers()
{
    //num is the result of the first routine to finish
    var num = await Async.Context.WaitForAny(GetTheFirstNumber, GetTheSecondNumber, GetTheThirdNumber);
    Debug.Log(num);
}

public async Task<int> GetTheFirstNumber()
{
    await Async.Context.WaitForSeconds(3);
    return 1;
}

public async Task<int> GetTheSecondNumber()
{
    await Async.Context.WaitForSeconds(2);
    return 2;
}

public async Task<int> GetTheThirdNumber()
{
    await Async.Context.WaitForSeconds(1);
    return 3;
}
```

## Waiting on Event/Callbacks
AsyncRoutines provides the helper type IResumer to allow for awaiting events/callbacks.
```cs
public IResumer resumer = null;

public async Task WaitForCallback()
{
    resumer = Async.GetResumer();
    await Async.Context.WaitForResumer(resumer);
    Async.ReleaseResumer(resumer);
    resumer = null;
}

public void OnCallback()
{
    resumer.Resume();
}
```
```cs
UnityEvent unityEvent;

public async Task WaitForUnityEvent()
{
    var resumer = Async.GetResumer();
    unityEvent.AddListener(resumer.Resume);
    await Async.Context.WaitForResumer(resumer);
    Async.ReleaseResumer(resumer);
}
```
```cs
event Action<string> strEvent;

public async Task WaitForEvent()
{
    var resumer = Async.GetResumer<string>();
    strEvent += resumer.Resume;
    await Async.Context.WaitForResumer(resumer);
    Async.ReleaseResumer(resumer);
}
```
Notice that IResumers are pooled and should be released when not needed. They can be re-used multiple times, but need to have their Reset() method called after each call to WaitForResumer.

IResumers are also "smart" about being called before being awaited upon.
```cs
public IResumer resumer = null;

public async Task WaitForCallback()
{
    resumer = Async.GetResumer();
    WaitForTime(); //Could call resumer.Resume immediately
    await Async.Context.WaitForResumer(resumer); //Detects that resumer was already called and doesn't wait
    Async.ReleaseResumer(resumer);
}

public async void WaitForTime()
{
    await Async.Context.WaitForSeconds(0); //Won't actually wait since time is zero
    resumer.Resume();
}
```
In this example, resumer.Resume() gets called before being awaited on. In this case it's 'marked' as resumed and WaitForResumer() will return immediately.

## Cleanup and Error Handling
RunAsync takes an optional onStop callback, which is always called when a routine ends, regardless of how it ended. This is a good place to do any cleanup. It is passed an Exception as it's only argument. If the routine threw an unhandled exception, it will be received there. Otherwise it will be null. If onStop is not set and an exception occurs, it will be reported using Unity's Debug.LogException.

Call stacks in exceptions from async routines are not very useful. To help with this, set Async.EnableTracing to true. This will add additional info to the exception to help trace where it came from. But, there's a small performance hit for using this, so it is off by default.

## Adding WaitFor... Methods
AsyncRoutines.IContext can be extended to add additional WaitFor methods to Async.Context. For example, here is how WaitForSeconds and WaitForAsyncOperation are implemented:
```cs
public static class IContextExtensions
{
    public static async Task WaitForSeconds(this IContext context, float seconds)
    {
        var endTime = Time.time + seconds;
        while (Time.time < endTime)
        {
            await context.WaitForNextFrame();
        }
    }

    public static async Task WaitForAsyncOperation(this IContext context, AsyncOperation asyncOperation)
    {
        if (asyncOperation.isDone)
        {
            return;
        }
        var resumer = Async.GetResumer<AsyncOperation>();
        asyncOperation.completed += resumer.Resume;
        await context.WaitForResumer(resumer);
        Async.ReleaseResumer(resumer);
    }
}
```

## Using Routines Outside of Behaviours
AsyncBehavior is a simple wrapper around an AsyncManager object. If you want to manage your own routines without using a component, you can use AsyncManager to do so, but you must call Update(), Flush() and StopAll() yourself at appropriate times. Flush() should be called after all Updates (usually in LateUpdate).

## Notes
- Individual routines can be stopped via the Stop() method on the handle returned from RunAsync().
- Do not use C#'s WhenAny/WhenAll in routines or you're going to have a bad time.
- Task.Delay should probably be avoided as well in favor of WaitForSeconds which uses Unity's game time.
