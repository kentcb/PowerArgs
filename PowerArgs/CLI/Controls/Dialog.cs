﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerArgs.Cli
{
    public class DialogButton
    {
        public string DisplayText { get; set; }
        public string Id { get; set; }
    }

    // todo - restrict the focus system to include only the dialog buttons so the dialog feels more modal
    public class Dialog : ConsolePanel
    {
        public int MaxHeight { get; set; } 

        public bool AllowEscapeToCancel { get; set; }

        public Event Cancelled { get; private set; } = new Event();

        private Button closeButton;

        private int myFocusStackDepth;

        public Dialog(ConsoleControl content)
        {
            Add(content).Fill(padding: new Thickness(0, 0, 1, 1));
            closeButton = Add(new Button() { Text = "Close (ESC)",Background = Theme.DefaultTheme.H1Color, Foreground = ConsoleColor.Black }).DockToRight(padding: 1);
            closeButton.Pressed.SubscribeForLifetime(Escape, this.LifetimeManager);
            BeforeAddedToVisualTree.SubscribeForLifetime(OnBeforeAddedToVisualTree, this.LifetimeManager);
            AddedToVisualTree.SubscribeForLifetime(OnAddedToVisualTree, this.LifetimeManager);
            RemovedFromVisualTree.SubscribeForLifetime(OnRemovedFromVisualTree, this.LifetimeManager);
        }

        private void OnBeforeAddedToVisualTree()
        {
            Application.FocusManager.Push();
            myFocusStackDepth = Application.FocusManager.StackDepth;
            Application.FocusManager.GlobalKeyHandlers.PushForLifetime(ConsoleKey.Escape, null, Escape,LifetimeManager );
        }

        private void OnAddedToVisualTree()
        {
            if(Parent != Application.LayoutRoot)
            {
                throw new InvalidOperationException("Dialogs must be added to the LayoutRoot of an application");
            }

            if (MaxHeight > 0)
            {
                this.Height = Math.Min(MaxHeight, Application.LayoutRoot.Height - 2);
            }
            else
            {
                this.Height = Application.LayoutRoot.Height - 2;
            }

            this.CenterVertically();
            this.FillHoriontally();
            ConsoleApp.Current.FocusManager.TryMoveFocus();

            Application.FocusManager.SubscribeForLifetime(nameof(FocusManager.StackDepth), () =>
            {
                if(Application.FocusManager.StackDepth != myFocusStackDepth)
                {
                    closeButton.Background = Application.Theme.DisabledColor;
                }
                else
                {
                    closeButton.Background = Application.Theme.H1Color;
                }
            }, this.LifetimeManager);

        }

        public void OnRemovedFromVisualTree()
        {
            Application.FocusManager.Pop();
        }

        private void Escape()
        {
            if (AllowEscapeToCancel)
            {
                Cancelled.Fire();
                ConsoleApp.Current.LayoutRoot.Controls.Remove(this);
            }
        }



        protected override void OnPaint(ConsoleBitmap context)
        {
            context.Pen = new ConsoleCharacter(' ', null, myFocusStackDepth == Application.FocusManager.StackDepth ? Theme.DefaultTheme.H1Color : Theme.DefaultTheme.DisabledColor);
            context.DrawLine(0, 0, Width, 0);
            context.DrawLine(0, Height-1, Width, Height-1);
            base.OnPaint(context);
        }

        public static void ConfirmYesOrNo(string message, Action yesCallback, Action noCallback = null, int maxHeight = 10)
        {
            ConfirmYesOrNo(message.ToConsoleString(), yesCallback, noCallback, maxHeight);
        }

        public static void ConfirmYesOrNo(ConsoleString message, Action yesCallback, Action noCallback = null, int maxHeight = 10)
        {
            ShowMessage(message, (b) =>
            {
                if (b != null && b.Id == "y")
                {
                    yesCallback();
                }
                else if (noCallback != null)
                {
                    noCallback();
                }
            }, true, maxHeight, new DialogButton() { Id = "y", DisplayText = "Yes", }, new DialogButton() { Id = "n", DisplayText = "No" });
        }

        public static void ShowMessage(ConsoleString message, Action<DialogButton> resultCallback, bool allowEscapeToCancel = true, int maxHeight = 6, params DialogButton [] buttons)
        {
            if(buttons.Length == 0)
            {
                throw new ArgumentException("You need to specify at least one button");
            }

            ConsolePanel dialogContent = new ConsolePanel();

            Dialog dialog = new Dialog(dialogContent);
            dialog.MaxHeight = maxHeight;
            dialog.AllowEscapeToCancel = allowEscapeToCancel;
            dialog.Cancelled.SubscribeForLifetime(() => { resultCallback(null); }, dialog.LifetimeManager);

            ScrollablePanel messagePanel = dialogContent.Add(new ScrollablePanel()).Fill(padding: new Thickness(0, 0, 1, 3));
            Label messageLabel = messagePanel.ScrollableContent.Add(new Label() { Mode = LabelRenderMode.MultiLineSmartWrap, Text = message }).FillHoriontally(padding: new Thickness(3,3,0,0) );

            StackPanel buttonPanel = dialogContent.Add(new StackPanel() { Margin = 1, Height = 1, Orientation = Orientation.Horizontal }).FillHoriontally(padding: new Thickness(1,0,0,0)).DockToBottom(padding: 1);

            Button firstButton = null;
            foreach (var buttonInfo in buttons)
            {
                var myButtonInfo = buttonInfo;
                Button b = new Button() { Text = buttonInfo.DisplayText };
                b.Pressed.SubscribeForLifetime(() => 
                {
                    ConsoleApp.Current.LayoutRoot.Controls.Remove(dialog);
                    resultCallback(myButtonInfo);
                }, dialog.LifetimeManager);
                buttonPanel.Controls.Add(b);
                firstButton = firstButton ?? b;
            }
            ConsoleApp.Current.LayoutRoot.Controls.Add(dialog);
        }


        public static void ShowMessage(string message, Action doneCallback = null, int maxHeight = 12)
        {
            ShowMessage(message.ToConsoleString(), doneCallback, maxHeight);
        }

        public static void ShowMessage(ConsoleString message, Action doneCallback = null, int maxHeight = 12)
        {
            ShowMessage(message, (b) => { if (doneCallback != null) doneCallback(); },true,maxHeight, new DialogButton() { DisplayText = "ok" });
        }

        public static void ShowTextInput(ConsoleString message, Action<ConsoleString> resultCallback, Action cancelCallback = null, bool allowEscapeToCancel = true, int maxHeight = 12)
        {
            ShowRichTextInput(message, resultCallback, cancelCallback, allowEscapeToCancel, maxHeight, null);
        }

        public static void ShowRichTextInput(ConsoleString message, Action<ConsoleString> resultCallback, Action cancelCallback = null, bool allowEscapeToCancel = true, int maxHeight = 12, TextBox inputBox = null)
        {
            if (ConsoleApp.Current == null)
            {
                throw new InvalidOperationException("There is no console app running");
            }

            ConsolePanel content = new ConsolePanel();

            content.Width = ConsoleApp.Current.LayoutRoot.Width / 2;
            content.Height = ConsoleApp.Current.LayoutRoot.Height / 2;

            var dialog = new Dialog(content);
            dialog.MaxHeight = maxHeight;
            dialog.AllowEscapeToCancel = allowEscapeToCancel;
            dialog.Cancelled.SubscribeForLifetime(() => { if (cancelCallback != null) cancelCallback(); }, dialog.LifetimeManager);
            
            Label messageLabel = content.Add(new Label() { Text = message,  X = 2, Y = 2 });
            if (inputBox == null)
            {
                inputBox = new TextBox() { Foreground = ConsoleColor.Black, Background = ConsoleColor.White };
            }

            content.Add(inputBox).CenterHorizontally();
            inputBox.Y = 4;

            content.SynchronizeForLifetime(nameof(Bounds), () => { inputBox.Width = content.Width - 4; }, content.LifetimeManager);

            inputBox.KeyInputReceived.SubscribeForLifetime((k) =>
            {
                if (k.Key == ConsoleKey.Enter)
                {
                    resultCallback(inputBox.Value);
                    ConsoleApp.Current.LayoutRoot.Controls.Remove(dialog);
                }
            }, inputBox.LifetimeManager);

            ConsoleApp.Current.LayoutRoot.Controls.Add(dialog);
            inputBox.TryFocus();
        }
    }
}
