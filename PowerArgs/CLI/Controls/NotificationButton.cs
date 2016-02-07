﻿using System;
using System.Linq;
using System.Threading;

namespace PowerArgs.Cli
{
    public class NotificationButton : ConsolePanel
    {
        Button launcher;
        Spinner spinner;
        ProgressOperationsManager manager;
        ConsoleColor launcherFg;
        Timer resetTimer;
        public NotificationButton(ProgressOperationsManager manager)
        {
            this.manager = manager;
            
            launcher = Add(new Button());
            launcher.Shortcut = new KeyboardShortcut(ConsoleKey.N, ConsoleModifiers.Alt);
            launcher.Activated.SubscribeForLifetime(NotificationButton_Activated, LifetimeManager);
            launcherFg = launcher.Foreground;
            spinner = Add(new Spinner() { IsVisible = false, IsSpinning = false, CanFocus = false, X = 1, Foreground = ConsoleColor.Cyan });
            Manager_ProgressOperationsChanged();
            manager.ProgressOperationsChanged += Manager_ProgressOperationsChanged;
        }

        public override void OnAddedToVisualTree()
        {
            base.OnAddedToVisualTree();
            launcher.Synchronize(nameof(Bounds), () => { this.Size = launcher.Size; });
            manager.ProgressOperationStatusChanged.Subscribe((op) =>
            {
                if(op.State == OperationState.Completed)
                {
                    launcher.Foreground = ConsoleColor.Green;
                    if(resetTimer != null)
                    {
                        Application.MessagePump.ClearTimeout(resetTimer);
                        resetTimer = null;
                    }
                    resetTimer = Application.MessagePump.SetTimeout(ResetLaundherFG, TimeSpan.FromSeconds(5));
                }
            });
        }

        private void ResetLaundherFG()
        {
            launcher.Foreground = launcherFg;
        }

        public override void OnRemovedFromVisualTree()
        {
            base.OnRemovedFromVisualTree();
        }

        private void Manager_ProgressOperationsChanged()
        {
            int numberOfOperations = manager.Operations.Count;
            int numberOfInProgressOperations = manager.Operations.Where(o => o.State == OperationState.InProgress).Count();

            if(numberOfInProgressOperations == 0)
            {
                spinner.IsSpinning = false;
                spinner.IsVisible = false;
                launcher.Text = ""+numberOfOperations+ (numberOfOperations == 1 ? " notification" : " notifications");
            }
            else
            {
                spinner.IsVisible = true;
                spinner.IsSpinning = true;
                launcher.Text = " "+numberOfOperations+ (numberOfOperations == 1 ? " notification" : " notifications");
            }
        }

        private void NotificationButton_Activated()
        {
            var app = Application as ConsolePageApp;
            if (app == null) throw new NotSupportedException("NotificationButton can only be used in a ConsolePageApp");
            app.PageStack.CurrentPage.ShowProgressOperationsDialog();
        }
    }
}
