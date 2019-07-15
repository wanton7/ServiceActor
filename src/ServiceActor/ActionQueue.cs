﻿using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks.Dataflow;

namespace ServiceActor
{
    public class ActionQueue
    {
        private readonly ActionBlock<InvocationItem> _actionQueue;
        private int? _executingActionThreadId;
        private InvocationItem _executingInvocationItem;

        public class InvocationItem
        {
            public InvocationItem(
                Action action,
                IServiceActorWrapper target,
                string typeOfObjectToWrap,
                bool keepContextForAsyncCalls = true)
            {
                Action = action;
                Target = target;
                TypeOfObjectToWrap = typeOfObjectToWrap;
                KeepContextForAsyncCalls = keepContextForAsyncCalls;
            }

            public InvocationItem(
                Action action,
                bool keepContextForAsyncCalls = true)
            {
                Action = action;
                KeepContextForAsyncCalls = keepContextForAsyncCalls;
            }

            public Action Action { get; private set; }

            public IServiceActorWrapper Target { get; private set; }

            public string TypeOfObjectToWrap { get; private set; }

            public bool KeepContextForAsyncCalls { get; private set; }

            private readonly Queue<IPendingOperation> _pendingOperations = new Queue<IPendingOperation>();

            public void EnqueuePendingOperation(IPendingOperation pendingOperation) => 
                _pendingOperations.Enqueue(pendingOperation);

            public bool WaitForPendingOperationCompletion()
            {
                foreach (var pendingOperation in _pendingOperations)
                {
                    pendingOperation.WaitForCompletion();
                }

                return _pendingOperations.Any();
            }

            public object GetLastPendingOperationResult()
            {
                var lastPendingOperation = _pendingOperations
                    .OfType<IPendingOperationWithResult>()
                    .LastOrDefault();

                if (lastPendingOperation == null)
                {
                    throw new InvalidOperationException("Unable to get result of the pending operation");
                }

                return lastPendingOperation.GetResult();
            }
        }

        public ActionQueue()
        {
            _actionQueue = new ActionBlock<InvocationItem>(invocation =>
            {
                if (invocation.KeepContextForAsyncCalls)
                {
                    //Console.WriteLine($"Current Thread ID Before action.Invoke: {Thread.CurrentThread.ManagedThreadId}");
                    _executingActionThreadId = Thread.CurrentThread.ManagedThreadId;

                    try
                    {
                        //System.Diagnostics.Debug.WriteLine($"-----Executing {invocation.Target?.WrappedObject}({invocation.TypeOfObjectToWrap}) {invocation.Action.Method}...");
                        if (_actionCallMonitor != null)
                        {
                            var callDetails = new CallDetails(this, invocation.Target, invocation.Target?.WrappedObject, invocation.TypeOfObjectToWrap, invocation.Action);
                            _actionCallMonitor?.EnterMethod(callDetails);
                        }
                        _executingInvocationItem = invocation;
                        AsyncContext.Run(invocation.Action);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(ex);
                    }

                    //System.Diagnostics.Debug.WriteLine($"-----Executed {invocation.Target?.WrappedObject}({invocation.TypeOfObjectToWrap}) {invocation.Action.Method}");
                    if (_actionCallMonitor != null)
                    {
                        var callDetails = new CallDetails(this, invocation.Target, invocation.Target?.WrappedObject, invocation.TypeOfObjectToWrap, invocation.Action);
                        _actionCallMonitor?.ExitMethod(callDetails);
                    }
                    _executingActionThreadId = null;
                    //action.Invoke();
                    //Console.WriteLine($"Current Thread ID After action.Invoke: {Thread.CurrentThread.ManagedThreadId}");
                }
                else
                {
                    invocation.Action();
                }
            });
        }

        public void Stop()
        {
            _actionQueue.Complete();
        }

        public InvocationItem Enqueue(IServiceActorWrapper target, string typeOfObjectToWrap, Action action, bool keepContextForAsyncCalls = true)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (typeOfObjectToWrap == null)
            {
                throw new ArgumentNullException(nameof(typeOfObjectToWrap));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (Thread.CurrentThread.ManagedThreadId == _executingActionThreadId)
            {
                //if the calling thread is the same as the first executing action then just pass thru
                action();
                return _executingInvocationItem;
            }

            var invocationItem = new InvocationItem(
                action, 
                target,
                typeOfObjectToWrap,
                keepContextForAsyncCalls);

            _actionQueue.Post(invocationItem);

            return invocationItem;
        }

        public InvocationItem Enqueue(Action action, bool keepContextForAsyncCalls = true)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (Thread.CurrentThread.ManagedThreadId == _executingActionThreadId)
            {
                //if the calling thread is the same as the first executing action then just pass thru
                action();
                return _executingInvocationItem;
            }

            var invocationItem = new InvocationItem(
                action,
                keepContextForAsyncCalls
            );

            _actionQueue.Post(invocationItem);

            return invocationItem;
        }

        #region Calls Monitor
        private static IActionCallMonitor _actionCallMonitor;
        public static void BeginMonitor(IActionCallMonitor actionCallMonitor)
        {
            _actionCallMonitor = actionCallMonitor ?? throw new ArgumentNullException(nameof(actionCallMonitor));
        }

        public static void ExitMonitor(IActionCallMonitor actionCallMonitor)
        {
            if (actionCallMonitor == null)
            {
                throw new ArgumentNullException(nameof(actionCallMonitor));
            }

            if (actionCallMonitor != _actionCallMonitor)
            {
                throw new InvalidOperationException();
            }

            _actionCallMonitor = null;
        }
        #endregion

        #region Pending Operations
        public void RegisterPendingOperation(IPendingOperation pendingOperation)
        {
            if (pendingOperation == null)
            {
                throw new ArgumentNullException(nameof(pendingOperation));
            }

            _executingInvocationItem.EnqueuePendingOperation(pendingOperation);
        }

        public void RegisterPendingOperation(WaitHandle waitHandle, int timeoutMilliseconds = 0, Action<bool> actionOnCompletion = null)
        {
            RegisterPendingOperation(new WaitHandlerPendingOperation(waitHandle, timeoutMilliseconds, actionOnCompletion));
        }

        public void RegisterPendingOperation<T>(WaitHandle waitHandle, Func<T> getResultFunction, int timeoutMilliseconds = 0, Action<bool> actionOnCompletion = null)
        {
            RegisterPendingOperation(new WaitHandlePendingOperation<T>(waitHandle, getResultFunction, timeoutMilliseconds, actionOnCompletion));
        }
        #endregion
    }
}