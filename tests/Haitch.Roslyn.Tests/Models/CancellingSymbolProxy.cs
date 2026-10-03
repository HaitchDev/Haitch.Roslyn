using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Haitch.Roslyn.Tests.Models;

/// <summary>
/// Forwards every call to a real symbol and cancels a token the first time a named member is used. The
/// BCL token cannot report "cancelled after N reads", so this is the seam that lets a test cancel after
/// a model's entry check and prove the later per-member checks.
/// </summary>
public class CancellingSymbolProxy : DispatchProxy
{
    private object? _target;
    private string? _cancelOnMember;
    private CancellationTokenSource? _source;

    public static T Create<T>(T target, string cancelOnMember, CancellationTokenSource source)
        where T : class
    {
        T proxy = Create<T, CancellingSymbolProxy>();
        CancellingSymbolProxy self = (CancellingSymbolProxy)(object)proxy;
        self._target = target;
        self._cancelOnMember = cancelOnMember;
        self._source = source;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == _cancelOnMember)
        {
            _source!.Cancel();
        }

        try
        {
            return targetMethod.Invoke(_target, args);
        }
        catch (TargetInvocationException exception)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException!).Throw();

            throw;
        }
    }
}
