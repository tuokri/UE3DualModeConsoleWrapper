// Copyright (C) 2026 Tuomo Kriikkula
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace UE3DualModeConsoleWrapper;

internal sealed class ConsoleInterruptController : IDisposable
{
    private readonly Action _forceShutdown;
    private readonly Action _requestGracefulShutdown;
    private readonly Action<string> _writeMessage;
    private int _state;

    public ConsoleInterruptController(
        Action requestGracefulShutdown,
        Action forceShutdown,
        Action<string>? writeMessage = null,
        bool registerWithConsole = false)
    {
        ArgumentNullException.ThrowIfNull(requestGracefulShutdown);
        ArgumentNullException.ThrowIfNull(forceShutdown);
        _requestGracefulShutdown = requestGracefulShutdown;
        _forceShutdown = forceShutdown;
        _writeMessage = writeMessage ?? Console.Error.WriteLine;
        if (registerWithConsole)
        {
            Console.CancelKeyPress += Handle;
        }
    }

    public bool ShutdownRequested => Volatile.Read(ref _state) != 0;

    public void Handle(object? sender, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        HandleInterrupt();
    }

    public void HandleInterrupt()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
        {
            _writeMessage("Exiting... Press Ctrl+C again to force exit.");
            _requestGracefulShutdown();
            return;
        }

        if (Interlocked.CompareExchange(ref _state, 2, 1) == 1)
        {
            _writeMessage("Forcing exit...");
            _forceShutdown();
        }
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= Handle;
    }
}
