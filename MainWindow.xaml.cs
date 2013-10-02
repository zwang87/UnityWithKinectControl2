using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit;
using Microsoft.Kinect.Toolkit.Interaction;
using Microsoft.Kinect.Toolkit.Controls;

namespace KinectController
{
    public enum KinectDepthTreatment
    {
        ClampUnreliableDepths = 0,
        TintUnreliableDepths,
        DisplayAllDepths
    }


    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private KinectSensor _sensor;
        private InteractionStream _interactionStream;

        private Skeleton[] _skeletons;
        private UserInfo[] _userInfos;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitKinect()
        {
            _sensor = KinectSensor.KinectSensors.FirstOrDefault();
            if (null == _sensor)
            {
                MessageBox.Show("No Kinect Sensor Detected!");
                Close();
                return;
            }

            _skeletons = new Skeleton[_sensor.SkeletonStream.FrameSkeletonArrayLength];
            _userInfos = new UserInfo[InteractionStream.FrameUserInfoArrayLength];

            //_sensor.DepthStream.Range = DepthRange.Near;
            _sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
            _sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
            _sensor.SkeletonStream.EnableTrackingInNearRange = true;
            _sensor.SkeletonStream.Enable();

            _interactionStream = new InteractionStream(_sensor, new InteractionClient());
            _interactionStream.InteractionFrameReady += InteractionStreamOnInteractionFrameReady;

            _sensor.DepthFrameReady += SensorOnDepthFrameReady;
            _sensor.SkeletonFrameReady += SensorOnSkeletonFrameReady;

            _sensor.Start();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            InitKinect();
        }

        private void SensorOnSkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (null == skeletonFrame)
                    return;
                try
                {
                    skeletonFrame.CopySkeletonDataTo(_skeletons);
                    _interactionStream.ProcessSkeleton(_skeletons, _sensor.AccelerometerGetCurrentReading(), skeletonFrame.Timestamp);
                }
                catch (System.Exception ex)
                {
                    //System.Environment.Exit(-1);
                }
            }
        }


        private DepthImagePixel[] pixelData;
        private DepthImageFormat lastImageFormat;
        private byte[] depthFrame32;
        private WriteableBitmap outputBitmap;
        private static readonly int Bgr32BytesPerPixel = (PixelFormats.Bgr32.BitsPerPixel + 7) / 8;

        private DepthColorizer colorizer = new DepthColorizer();
        private KinectDepthTreatment depthTreatment = KinectDepthTreatment.ClampUnreliableDepths;

        private void SensorOnDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            int imageWidth = 0;
            int imageHeight = 0;
            bool haveNewFormat = false;
            int minDepth = 0;
            int maxDepth = 0;
            using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
            {
                if (null == depthFrame)
                    return;
                try
                {
                    this._interactionStream.ProcessDepth(depthFrame.GetRawPixelData(), depthFrame.Timestamp);
                }
                catch (InvalidOperationException)
                {
                    //System.Environment.Exit(-1);
                }

                imageWidth = depthFrame.Width;
                imageHeight = depthFrame.Height;
                haveNewFormat = (lastImageFormat != depthFrame.Format);
                if (haveNewFormat)
                {
                    pixelData = new DepthImagePixel[depthFrame.PixelDataLength];
                    depthFrame32 = new byte[depthFrame.Width * depthFrame.Height * Bgr32BytesPerPixel];
                    lastImageFormat = depthFrame.Format;
                }
                depthFrame.CopyDepthImagePixelDataTo(pixelData);
                minDepth = depthFrame.MinDepth;
                maxDepth = depthFrame.MaxDepth;
            }

            if (imageWidth != 0)
            {
                this.Dispatcher.Invoke((Action)(() =>
                    {
                        colorizer.ConvertDepthFrame(this.pixelData, minDepth, maxDepth, this.depthTreatment, this.depthFrame32);
                        if (haveNewFormat)
                        {
                            // A WriteableBitmap is a WPF construct that enables resetting the Bits of the image.
                            // This is more efficient than creating a new Bitmap every frame.
                            this.outputBitmap = new WriteableBitmap(
                                imageWidth,
                                imageHeight,
                                96, // DpiX
                                96, // DpiY
                                PixelFormats.Bgr32,
                                null);

                            depthImage.Source = this.outputBitmap;
                        }

                        this.outputBitmap.WritePixels(
                            new Int32Rect(0, 0, imageWidth, imageHeight),
                            this.depthFrame32,
                            imageWidth * Bgr32BytesPerPixel,
                            0);
                        depthImage.Source = outputBitmap;
                    }));
            }
        }

        private Dictionary<int, InteractionHandEventType> _lastLeftHandEvents = new Dictionary<int, InteractionHandEventType>();
        private Dictionary<int, InteractionHandEventType> _lastRightHandEvents = new Dictionary<int, InteractionHandEventType>();

        private void InteractionStreamOnInteractionFrameReady(object sender, InteractionFrameReadyEventArgs e)
        {
            using (var interactionFrame = e.OpenInteractionFrame())
            {
                if (null == interactionFrame)
                    return;
                interactionFrame.CopyInteractionDataTo(_userInfos);
            }
            StringBuilder showInfo = new StringBuilder();
            bool hasUser = false;
            foreach (var userInfo in _userInfos)
            {
                var userId = userInfo.SkeletonTrackingId;
                if (0 == userId)
                    continue;
                hasUser = true;
                showInfo.AppendLine("User ID = " + userId);
                showInfo.AppendLine("  Hands: ");
                var handPointers = userInfo.HandPointers;
                if (0 == handPointers.Count)
                    showInfo.AppendLine("  No hands");
                else
                {
                    foreach (var hand in handPointers)
                    {
                        var lastHandEvents = (hand.HandType == InteractionHandType.Left ? _lastLeftHandEvents : _lastRightHandEvents);
                        if (hand.HandEventType != InteractionHandEventType.None)
                            lastHandEvents[userId] = hand.HandEventType;
                        var lastHandEvent = lastHandEvents.ContainsKey(userId) ? lastHandEvents[userId] : InteractionHandEventType.None;
                        showInfo.AppendLine();
                        showInfo.AppendLine("    HandType: " + hand.HandType);
                        showInfo.AppendLine("    LastHandEventType: " + lastHandEvent);
                        showInfo.AppendLine("    IsActive: " + hand.IsActive);
                        showInfo.AppendLine("    IsPrimaryForUser: " + hand.IsPrimaryForUser);
                        showInfo.AppendLine("    IsInteractive: " + hand.IsInteractive);
                        showInfo.AppendLine("    PressExtent: " + hand.PressExtent.ToString("N4"));
                        showInfo.AppendLine("    IsPressed: " + hand.IsPressed);
                        showInfo.AppendLine("    IsTracked: " + hand.IsTracked);
                        showInfo.AppendLine("    X: " + hand.X.ToString("N4"));
                        showInfo.AppendLine("    Y: " + hand.Y.ToString("N4"));
                        showInfo.AppendLine("    RawX: " + hand.RawX.ToString("N4"));
                        showInfo.AppendLine("    RawY: " + hand.RawY.ToString("N4"));
                        showInfo.AppendLine("    RawZ: " + hand.RawZ.ToString("N4"));
                    }
                }
                textBlock.Text = showInfo.ToString();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (null != _sensor)
                _sensor.Stop();
            System.Environment.Exit(0);
        }
    }

    
}
