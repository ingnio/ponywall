namespace pylorak.TinyWall.Views
{
    /// <summary>
    /// Lifecycle interface for pages hosted in <see cref="MainWindow"/>'s
    /// content area. Pages are created lazily on first navigation and kept
    /// alive after, so timers/subscriptions should start in
    /// <see cref="OnNavigatedTo"/> and pause in <see cref="OnNavigatedFrom"/>
    /// to avoid burning CPU while off-screen.
    /// </summary>
    internal interface INavigablePage
    {
        /// <summary>
        /// Called when this page becomes the active visible page.
        /// Resume timers, refresh data, etc.
        /// </summary>
        void OnNavigatedTo();

        /// <summary>
        /// Called when the user navigates away from this page.
        /// Pause timers, cancel pending operations, etc.
        /// </summary>
        void OnNavigatedFrom();

        /// <summary>
        /// One-time initialization with app-level dependencies.
        /// Called once before the first <see cref="OnNavigatedTo"/>.
        /// </summary>
        void Initialize(Controller? controller, ServerConfiguration? config);
    }
}
