﻿
using System;
using System.Threading.Tasks;

namespace PowerArgs.Cli
{
    // todos before fully supporting the .Cli namespace
    // 
    // Theme should be observable and should result in a Paint when changed.  Controls might need to react as well.
    // Do another refactoring and functionality pass on data sources and caching.  It't not ready. 
    // Samples for different data sources (e.g. An azure table, a file system)      // Lots of testing
    // Samples for different data sources (e.g. An azure table, a file system)   
    // Hook up the 'latest response' debouncer for the grid 
    // Finish converting events and property changed handlers to Events (with a capital E)   
    // Lots of testing
    // Get rid of that time profiler experiment
    // Final code review and documentation

    /// <summary>
    /// A class representing a console application that uses a message pump to synchronize work on a UI thread
    /// </summary>
    public class ConsoleApp : CliMessagePump
    {
        [ThreadStatic]
        private static ConsoleApp _current;

        /// <summary>
        /// Gets a reference to the current app running on this thread.  This will only be populated by the thread
        /// that is running the message pump (i.e. it will never be your main thread).
        /// </summary>
        public static ConsoleApp Current
        {
            get
            {
                return _current;
            }
        }

        /// <summary>
        /// An event that fires when the application is about to stop, before the console is wiped
        /// </summary>
        public Event Stopping { get; private set; } = new Event();

        /// <summary>
        /// An event that fires after the message pump is completely stopped and the console is wiped
        /// </summary>
        public Event Stopped { get; private set; } = new Event();

        /// <summary>
        /// An event that fires when a control is added to the visual tree
        /// </summary>
        public Event<ConsoleControl> ControlAdded { get; private set; } = new Event<ConsoleControl>();

        /// <summary>
        /// An event that fires when a control is removed from the visual tree
        /// </summary>
        public Event<ConsoleControl> ControlRemoved { get; private set; } = new Event<ConsoleControl>();

        /// <summary>
        /// Gets the bitmap that will be painted to the console
        /// </summary>
        public ConsoleBitmap Bitmap { get; private set; }

        /// <summary>
        /// Gets the root panel that contains the controls being used by the app
        /// </summary>
        public ConsolePanel LayoutRoot { get; private set; }

        /// <summary>
        /// Gets the focus manager used to manage input focus
        /// </summary>
        public FocusManager FocusManager { get; private set; }

        /// <summary>
        /// If set to true then the app will automatically update its layout to fill the entire window.  If false the app
        /// will not react to resizing, which means it may clip or wrap in unexpected ways when the window is resized.
        /// 
        /// If you use the constructor that takes no parameters then this is set to true and assumes you want to take the
        /// whole window and respond to window size changes.  If you use the constructor that takes in coordinates and boudnds
        /// then it is set to false and it is assumed that you only want the app to live within those bounds
        /// </summary>
        public bool AutoFillOnConsoleResize { get; set; }

        /// <summary>
        /// Gets or sets the theme
        /// </summary>
        public Theme Theme { get { return Get<Theme>(); } set { Set(value); } }

        /// <summary>
        /// Gets or set whether or not to give focus to a control when the app starts.  The default is true.
        /// </summary>
        public bool SetFocusOnStart { get; set; }

        /// <summary>
        /// Creates a new console app given a set of boundaries
        /// </summary>
        /// <param name="x">The left position on the target console to bound this app</param>
        /// <param name="y">The right position on the target console to bound this app</param>
        /// <param name="w">The width of the app</param>
        /// <param name="h">The height of the app</param>
        public ConsoleApp(int x, int y, int w, int h) : base(ConsoleProvider.Current)
        {
            SetFocusOnStart = true;
            Theme = new Theme();
            Bitmap = new ConsoleBitmap(x, y, w, h);
            LayoutRoot = new ConsolePanel { Width = w, Height = h };
            FocusManager = new FocusManager();
            LayoutRoot.Application = this;
            AutoFillOnConsoleResize = false;
            FocusManager.SubscribeForLifetime(nameof(FocusManager.FocusedControl), Paint, LifetimeManager);
            LayoutRoot.Controls.BeforeAdded.SubscribeForLifetime((c) => { c.Application = this; c.BeforeAddedToVisualTreeInternal(); }, LifetimeManager);
            LayoutRoot.Controls.BeforeRemoved.SubscribeForLifetime((c) => { c.BeforeRemovedFromVisualTreeInternal(); }, LifetimeManager);
            LayoutRoot.Controls.Added.SubscribeForLifetime(ControlAddedToVisualTree, LifetimeManager);
            LayoutRoot.Controls.Removed.SubscribeForLifetime(ControlRemovedFromVisualTree, LifetimeManager);
            WindowResized.SubscribeForLifetime(HandleDebouncedResize, LifetimeManager);
        }

        /// <summary>
        /// Creates a ConsoleApp from markup and a view model
        /// </summary>
        /// <param name="markup">The xml markup that defines the app's view</param>
        /// <param name="viewModel">The view model object that defines the code behind the view</param>
        /// <returns>An app where the view has been bound to the view model</returns>
        public static ConsoleApp FromMvVm(string markup, object viewModel)
        {
            var ret = MarkupParser.Parse(markup, viewModel);
            return ret;
        }

        /// <summary>
        /// Creates a full screen console app that will automatically adjust its layout if the window size changes
        /// </summary>
        public ConsoleApp() : this(0,0,ConsoleProvider.Current.BufferWidth, ConsoleProvider.Current.WindowHeight-1)
        {
            this.AutoFillOnConsoleResize = true;
        }

        /// <summary>
        /// Starts the app, asynchronously.
        /// </summary>
        /// <returns>A task that will complete when the app exits</returns>
        public override Task Start()
        {
            QueueAction(() =>
            {
                if (_current != null)
                {
                    throw new NotSupportedException("An application is already running on this thread.");
                }
                // ensures that the current app is set on the message pump thread
                _current = this;
            });

            if (SetFocusOnStart)
            {
                QueueAction(() => 
                {
                    FocusManager.TryMoveFocus();
                });
            }

            Paint();

            Task pumpTask = base.Start();

            var cleanupTask = pumpTask.ContinueWith((t) =>
            {
                 ExitInternal();
            });

            return cleanupTask;
        }

        private void HandleDebouncedResize()
        {
            if(AutoFillOnConsoleResize)
            {
                Bitmap.Resize(Bitmap.Console.BufferWidth, Bitmap.Console.WindowHeight - 1);
                this.LayoutRoot.Size = new Size(Bitmap.Console.BufferWidth, Bitmap.Console.WindowHeight - 1);
            }

            Paint();
        }

        /// <summary>
        /// Queues up a request to paint the app.  The system will dedupe multiple paint requests when there are multiple in the pump's work queue
        /// </summary>
        public void Paint()
        {
            QueueAction(new PaintMessage(PaintInternal));
        }

        private void ControlAddedToVisualTree(ConsoleControl c)
        {
            c.Application = this;

            if (c is ConsolePanel)
            {
                var childPanel = c as ConsolePanel;
                childPanel.Controls.SynchronizeForLifetime((cp) => { ControlAddedToVisualTree(cp); }, (cp) => { ControlRemovedFromVisualTree(cp); }, () => { }, c.LifetimeManager);
            }

            FocusManager.Add(c);
            c.AddedToVisualTreeInternal();

            ControlAdded.Fire(c);
        }

        private void ControlRemovedFromVisualTree(ConsoleControl c)
        {
            if (ControlRemovedFromVisualTreeRecursive(c))
            {
                FocusManager.TryRestoreFocus();
            }
        }

        private bool ControlRemovedFromVisualTreeRecursive(ConsoleControl c)
        {
            bool focusChanged = false;

            if (c is ConsolePanel)
            {
                foreach (var child in (c as ConsolePanel).Controls)
                {
                    focusChanged = ControlRemovedFromVisualTreeRecursive(child) || focusChanged;
                }
            }

            if (FocusManager.FocusedControl == c)
            {
                FocusManager.ClearFocus();
                focusChanged = true;
            }

            FocusManager.Remove(c);

            c.RemovedFromVisualTreeInternal();
            c.Application = null;
            ControlRemoved.Fire(c);
            c.Dispose();
            return focusChanged;
        }

        /// <summary>
        /// Handles key input for the application
        /// </summary>
        /// <param name="info">The key that was pressed</param>
        protected override void HandleKeyInput(ConsoleKeyInfo info)
        {
            if (FocusManager.GlobalKeyHandlers.TryIntercept(info))
            {
                // great, it was handled
            }
            else if (info.Key == ConsoleKey.Tab)
            {
                FocusManager.TryMoveFocus(info.Modifiers.HasFlag(ConsoleModifiers.Shift) == false);
            }
            else if (info.Key == ConsoleKey.Escape)
            {
                Stop();
                return;
            }
            else if (FocusManager.FocusedControl != null)
            {
                FocusManager.FocusedControl.HandleKeyInput(info);
            }
            else
            {
                // not handled
            }
            Paint();
        }

        private void ExitInternal()
        {
            Stopping.Fire();
            using (var snapshot = Bitmap.CreateSnapshot())
            {
                Bitmap.CreateWiper().Wipe();
                Bitmap.Console.ForegroundColor = ConsoleString.DefaultForegroundColor;
                Bitmap.Console.BackgroundColor = ConsoleString.DefaultBackgroundColor;
            }
            _current = null;
            Stopped.Fire();
            Dispose();
        }

        private void PaintInternal()
        {

            Bitmap.Pen = new ConsoleCharacter(' ', null, Theme.BackgroundColor);
            Bitmap.FillRect(0, 0, LayoutRoot.Width, LayoutRoot.Height);
#if PROFILING
            using (new TimeProfiler("LayoutRoot.Paint"))
            {
#endif
                LayoutRoot.Paint(Bitmap);
#if PROFILING
            }
#endif

#if PROFILING
            using (new TimeProfiler("Bitmap.Paint"))
            {
#endif
                Bitmap.Paint();
#if PROFILING
            }
#endif
        }
    }
}
