﻿using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Threading;
using AppsTracker.Common.Utils;
using AppsTracker.Communication;
using AppsTracker.Data.Repository;
using AppsTracker.Domain.Settings;

namespace AppsTracker.Tracking
{
    [Export(typeof(IIdleNotifier))]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class IdleNotifier : IIdleNotifier
    {
        private const int TIMER_PERIOD = 1 * 1000;
        private const int TIMER_DELAY = 1 * 60 * 1000;
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;

        private readonly IAppSettingsService settingsService;
        private readonly ISyncContext syncContext;

        private bool disposed = false;
        private bool hooksRemoved = true;
        private bool idleEntered = false;

        private IntPtr keyboardHookHandle = IntPtr.Zero;
        private IntPtr mouseHookHandle = IntPtr.Zero;

        private Timer idleTimer;

        private readonly KeyboardHookCallback keyboardHookCallback;
        private readonly MouseHookCallback mouseHookCallback;

        public event EventHandler IdleEntered;
        public event EventHandler IdleStoped;

        [ImportingConstructor]
        public IdleNotifier(ISyncContext syncContext, IAppSettingsService settingsService)
        {
            this.syncContext = syncContext;
            this.settingsService = settingsService;
            keyboardHookCallback = new KeyboardHookCallback(KeyboardHookProc);
            mouseHookCallback = new MouseHookCallback(MouseHookProc);
            idleTimer = new Timer(CheckIdleState, null, TIMER_DELAY, TIMER_PERIOD);
        }

        private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
                Reset();
            return NativeMethods.CallNextHookEx(keyboardHookHandle, nCode, wParam, lParam);
        }


        private void CheckIdleState(object sender)
        {
            syncContext.Invoke(s =>
            {
                var idleTimeSpan = IdleTimeWatcher.GetIdleTimeSpan();
                if (idleTimeSpan >= TimeSpan.FromMilliseconds(settingsService.Settings.IdleTimer))
                {
                    idleEntered = true;
                    idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    SetHooks();
                    IdleEntered.InvokeSafely(this, EventArgs.Empty);
                }
            });
        }

        private void SetHooks()
        {
            if (keyboardHookHandle == IntPtr.Zero && mouseHookHandle == IntPtr.Zero)
            {
                using (var process = Process.GetCurrentProcess())
                {
                    using (var module = process.MainModule)
                    {
                        keyboardHookHandle = NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, keyboardHookCallback, NativeMethods.GetModuleHandle(module.ModuleName), 0);
                        mouseHookHandle = NativeMethods.SetWindowsHookEx(WH_MOUSE_LL, mouseHookCallback, NativeMethods.GetModuleHandle(module.ModuleName), 0);
                    }
                }
                hooksRemoved = false;
            }
        }


        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
                Reset();
            return NativeMethods.CallNextHookEx(mouseHookHandle, nCode, wParam, lParam);
        }

        private void Reset()
        {
            if (!idleEntered)
                return;

            idleEntered = false;
            RemoveHooks();
            idleTimer.Change(TIMER_DELAY, TIMER_PERIOD);
            IdleStoped.InvokeSafely(this, EventArgs.Empty);
        }


        private void RemoveHooks()
        {
            NativeMethods.UnhookWindowsHookEx(keyboardHookHandle);
            NativeMethods.UnhookWindowsHookEx(mouseHookHandle);

            keyboardHookHandle = IntPtr.Zero;
            mouseHookHandle = IntPtr.Zero;
            hooksRemoved = true;
        }

        ~IdleNotifier()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                idleTimer.Dispose();

                Delegate[] delegateBuffer = null;
                if (IdleEntered != null)
                {
                    delegateBuffer = IdleEntered.GetInvocationList();
                    foreach (EventHandler del in delegateBuffer)
                    {
                        IdleEntered -= del;
                    }
                    IdleEntered = null;
                }

                if (IdleStoped != null)
                {
                    delegateBuffer = IdleStoped.GetInvocationList();
                    foreach (EventHandler del in delegateBuffer)
                    {
                        IdleStoped -= del;
                    }
                    IdleStoped = null;
                }

                if (!hooksRemoved)
                    RemoveHooks();

                disposed = true;
            }
        }
    }
}
