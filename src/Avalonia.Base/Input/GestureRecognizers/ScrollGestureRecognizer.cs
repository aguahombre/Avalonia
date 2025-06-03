using System;
using System.Diagnostics;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Avalonia.Input.GestureRecognizers
{
    public class ScrollGestureRecognizer : GestureRecognizer
    {
        public const double InertialResistance = 0.15;
        internal const double DefaultInertialScrollSpeedEnd = 5;

        private bool _canHorizontallyScroll;
        private bool _canVerticallyScroll;
        private bool _isScrollInertiaEnabled;
        private readonly static int s_defaultScrollStartDistance = (int)((AvaloniaLocator.Current?.GetService<IPlatformSettings>()?.GetTapSize(PointerType.Touch).Height ?? 10) / 2);
        private int _scrollStartDistance = s_defaultScrollStartDistance;

        private bool _scrolling;
        private Point _trackedRootPoint;
        private IPointer? _tracking;
        private int _gestureId;
        private Point _pointerPressedPoint;
        private VelocityTracker? _velocityTracker;
        private Visual? _rootTarget;
        private bool _allowPointerGesture;
        private double _inertialScrollSpeedEnd = DefaultInertialScrollSpeedEnd;

        // Movement per second
        private Vector _inertia;
        private ulong? _lastMoveTimestamp;

        /// <summary>
        /// Defines the <see cref="CanHorizontallyScroll"/> property.
        /// </summary>
        public static readonly DirectProperty<ScrollGestureRecognizer, bool> CanHorizontallyScrollProperty =
            AvaloniaProperty.RegisterDirect<ScrollGestureRecognizer, bool>(nameof(CanHorizontallyScroll), 
                o => o.CanHorizontallyScroll, (o, v) => o.CanHorizontallyScroll = v);

        /// <summary>
        /// Defines the <see cref="CanVerticallyScroll"/> property.
        /// </summary>
        public static readonly DirectProperty<ScrollGestureRecognizer, bool> CanVerticallyScrollProperty =
            AvaloniaProperty.RegisterDirect<ScrollGestureRecognizer, bool>(nameof(CanVerticallyScroll),
                o => o.CanVerticallyScroll, (o, v) => o.CanVerticallyScroll = v);

        /// <summary>
        /// Defines the <see cref="IsScrollInertiaEnabled"/> property.
        /// </summary>
        public static readonly DirectProperty<ScrollGestureRecognizer, bool> IsScrollInertiaEnabledProperty =
            AvaloniaProperty.RegisterDirect<ScrollGestureRecognizer, bool>(nameof(IsScrollInertiaEnabled),
                o => o.IsScrollInertiaEnabled, (o,v) => o.IsScrollInertiaEnabled = v);

        /// <summary>
        /// Defines the <see cref="AllowPointerGesture"/> property.
        /// </summary>
        public static readonly DirectProperty<ScrollGestureRecognizer, bool> AllowPointerGestureProperty =
            AvaloniaProperty.RegisterDirect<ScrollGestureRecognizer, bool>(nameof(AllowPointerGesture),
                o => o.AllowPointerGesture, (o,v) => o.AllowPointerGesture = v);

        /// <summary>
        /// Defines the <see cref="ScrollStartDistance"/> property.
        /// </summary>
        public static readonly DirectProperty<ScrollGestureRecognizer, int> ScrollStartDistanceProperty =
            AvaloniaProperty.RegisterDirect<ScrollGestureRecognizer, int>(nameof(ScrollStartDistance),
                o => o.ScrollStartDistance, (o, v) => o.ScrollStartDistance = v,
                unsetValue: s_defaultScrollStartDistance);

        /// <summary>
        /// Defines the <see cref="InertialScrollSpeedEnd"/> property.
        /// </summary>
        public static readonly DirectProperty<ScrollGestureRecognizer, double> InertialScrollSpeedEndProperty =
            AvaloniaProperty.RegisterDirect<ScrollGestureRecognizer, double>(nameof(InertialScrollSpeedEnd),
                o => o.InertialScrollSpeedEnd, (o, v) => o.InertialScrollSpeedEnd = v,
                unsetValue: DefaultInertialScrollSpeedEnd);

        /// <summary>
        /// Gets or sets a value indicating whether the content can be scrolled horizontally.
        /// </summary>
        public bool CanHorizontallyScroll
        {
            get => _canHorizontallyScroll;
            set => SetAndRaise(CanHorizontallyScrollProperty, ref _canHorizontallyScroll, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the content can be scrolled vertically.
        /// </summary>
        public bool CanVerticallyScroll
        {
            get => _canVerticallyScroll;
            set => SetAndRaise(CanVerticallyScrollProperty, ref _canVerticallyScroll, value);
        }

        /// <summary>
        /// Gets or sets whether the gesture should include inertia in it's behavior.
        /// </summary>
        public bool IsScrollInertiaEnabled
        {
            get => _isScrollInertiaEnabled;
            set => SetAndRaise(IsScrollInertiaEnabledProperty, ref _isScrollInertiaEnabled, value);
        }

        /// <summary>
        /// Gets or sets whether the (mouse) pointer is allowed to generate scroll gestures.
        /// </summary>
        public bool AllowPointerGesture
        {
            get => _allowPointerGesture;
            set => SetAndRaise(AllowPointerGestureProperty, ref _allowPointerGesture, value);
        }

        /// <summary>
        /// Gets or sets a value indicating the distance the pointer moves before scrolling is started
        /// </summary>
        public int ScrollStartDistance
        {
            get => _scrollStartDistance;
            set => SetAndRaise(ScrollStartDistanceProperty, ref _scrollStartDistance, value);
        }

        // Pixels per second speed that is considered to be the stop of inertial scroll
        public double InertialScrollSpeedEnd
        {
            get => _inertialScrollSpeedEnd;
            set => SetAndRaise(InertialScrollSpeedEndProperty, ref _inertialScrollSpeedEnd, value);
        }

        protected override void PointerPressed(PointerPressedEventArgs e)
        {
            if (AllowPointerGesture || e.Pointer.Type == PointerType.Touch || e.Pointer.Type == PointerType.Pen) 
            {
                EndGesture();
                _tracking = e.Pointer;
                _gestureId = ScrollGestureEventArgs.GetNextFreeId();
                _rootTarget = (Visual?)(Target as Visual)?.VisualRoot;
                _trackedRootPoint = _pointerPressedPoint = e.GetPosition(_rootTarget);
                _velocityTracker = new VelocityTracker();
                _velocityTracker?.AddPosition(TimeSpan.FromMilliseconds(e.Timestamp), default);
            }
            System.Diagnostics.Trace.WriteLine($"AV-ScrollPress {_gestureId} {e.Pointer.IsPrimary} {GetHashCode():X8}");
        }

        protected override void PointerMoved(PointerEventArgs e)
        {
            //System.Diagnostics.Trace.WriteLine($"AV-ScrollMove {e.Pointer.Id} {e.Pointer.IsPrimary}");
            if (e.Pointer == _tracking)
            {
                var rootPoint = e.GetPosition(_rootTarget);
                if (!_scrolling)
                {
                    if (CanHorizontallyScroll && Math.Abs(_trackedRootPoint.X - rootPoint.X) > ScrollStartDistance)
                        _scrolling = true;
                    if (CanVerticallyScroll && Math.Abs(_trackedRootPoint.Y - rootPoint.Y) > ScrollStartDistance)
                        _scrolling = true;
                    if (_scrolling)
                    {                        
                        // Correct _trackedRootPoint with ScrollStartDistance, so scrolling does not start with a skip of ScrollStartDistance
                        _trackedRootPoint = new Point(
                            _trackedRootPoint.X - (_trackedRootPoint.X >= rootPoint.X ? ScrollStartDistance : -ScrollStartDistance),
                            _trackedRootPoint.Y - (_trackedRootPoint.Y >= rootPoint.Y ? ScrollStartDistance : -ScrollStartDistance));

                        Capture(e.Pointer);
                    }
                }

                if (_scrolling)
                {
                    var vector = _trackedRootPoint - rootPoint;

                    _velocityTracker?.AddPosition(TimeSpan.FromMilliseconds(e.Timestamp), _pointerPressedPoint - rootPoint);

                    _lastMoveTimestamp = e.Timestamp;
                    Target!.RaiseEvent(new ScrollGestureEventArgs(_gestureId, vector));
                    _trackedRootPoint = rootPoint;
                    e.Handled = true;
                }
            }
        }

        protected override void PointerCaptureLost(IPointer pointer)
        {
            System.Diagnostics.Trace.WriteLine($"AV-ScrollLost {pointer.Id} {pointer.IsPrimary}");
            if (pointer == _tracking) EndGesture();
        }

        void EndGesture()
        {
            _tracking = null;
            if (_scrolling)
            {
                _inertia = default;
                _scrolling = false;
                Target!.RaiseEvent(new ScrollGestureEndedEventArgs(_gestureId));
                _gestureId = 0;
                _lastMoveTimestamp = null;
                _rootTarget = null;
            }
            
        }


        protected override void PointerReleased(PointerReleasedEventArgs e)
        {
            System.Diagnostics.Trace.WriteLine($"AV-ScrollEnd {e.Pointer.Id} {e.Pointer.IsPrimary}");
            if (e.Pointer == _tracking && _scrolling)
            {
                _inertia = _velocityTracker?.GetFlingVelocity().PixelsPerSecond ?? Vector.Zero;

                e.Handled = true;
                if (_inertia == default
                    || double.IsNaN(_inertia.X) || double.IsNaN(_inertia.Y)
                    || double.IsInfinity(_inertia.X) || double.IsInfinity(_inertia.Y)
                    || e.Timestamp == 0
                    || _lastMoveTimestamp == 0
                    || e.Timestamp - _lastMoveTimestamp > 200
                    || !IsScrollInertiaEnabled)
                    EndGesture();
                else
                {
                    _tracking = null;
                    var savedGestureId = _gestureId;
                    var st = Stopwatch.StartNew();
                    var lastTime = TimeSpan.Zero;
                    Target!.RaiseEvent(new ScrollGestureInertiaStartingEventArgs(_gestureId, _inertia));
                    DispatcherTimer.Run(() =>
                    {
                        // Another gesture has started, finish the current one
                        if (_gestureId != savedGestureId)
                        {
                            System.Diagnostics.Trace.WriteLine($"AV-Inertia {_gestureId} {savedGestureId} {GetHashCode():X8}");
                            return false;
                        }

                        var elapsedSinceLastTick = st.Elapsed - lastTime;
                        lastTime = st.Elapsed;

                        var speed = _inertia * Math.Pow(InertialResistance, st.Elapsed.TotalSeconds);
                        var distance = speed * elapsedSinceLastTick.TotalSeconds;
                        var scrollGestureEventArgs = new ScrollGestureEventArgs(_gestureId, distance);
                    System.Diagnostics.Trace.WriteLine($"AV-Inertia {_gestureId} I={_inertia} S={speed} D={distance} T={st.Elapsed.TotalSeconds} P={Math.Pow(InertialResistance, st.Elapsed.TotalSeconds)}");
                        Target!.RaiseEvent(scrollGestureEventArgs);

                        if (!scrollGestureEventArgs.Handled || scrollGestureEventArgs.ShouldEndScrollGesture)
                        {
                            EndGesture();
                            return false;
                        }

                        // EndGesture using InertialScrollSpeedEnd only in the direction of scrolling
                        if (CanVerticallyScroll && CanHorizontallyScroll && Math.Abs(speed.X) < InertialScrollSpeedEnd && Math.Abs(speed.Y) <= InertialScrollSpeedEnd)
                        {
                            EndGesture();
                            return false;
                        }
                        else if (CanVerticallyScroll && Math.Abs(speed.Y) <= InertialScrollSpeedEnd)
                        {
                            EndGesture();
                            return false;
                        }
                        else if (CanHorizontallyScroll && Math.Abs(speed.X) < InertialScrollSpeedEnd)
                        {
                            EndGesture();
                            return false;
                        }

                        return true;
                    }, TimeSpan.FromMilliseconds(16), DispatcherPriority.Background);
                }
            }
        }
    }
}
