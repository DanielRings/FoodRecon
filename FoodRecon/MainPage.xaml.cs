using Microsoft.Band.Notifications;
using Microsoft.Band.Tiles;
using Microsoft.WindowsAzure.Messaging;
using Microsoft.WindowsAzure.MobileServices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.Devices.Geolocation;
using Windows.Devices.Geolocation.Geofencing;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Globalization;
using Windows.Globalization.DateTimeFormatting;
using Windows.Globalization.NumberFormatting;
using Windows.Networking.PushNotifications;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Microsoft.Band;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace FoodRecon
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private MobileServiceCollection<Breadcrumbs, Breadcrumbs> breadcrumbs;
        private IMobileServiceTable<Breadcrumbs> breadcrumbsTable = App.MobileService.GetTable<Breadcrumbs>();
        //private IMobileServiceSyncTable<Breadcrumbs> breadcrumbsTable = App.MobileService.GetSyncTable<Breadcrumbs>(); // offline sync
        // Proides access to location data
        private GeolocationAccessStatus accessStatus = GeolocationAccessStatus.Unspecified;
        private Geolocator _geolocator = null;
        private double lat;
        private double lon;

        // Band fields
        IBandClient bandClient;
        Guid myTileId;

        // Geofence fields
        private int secondsPerMinute = 60;
        private int secondsPerHour = 60 * 60;
        private int secondsPerDay = 24 * 60 * 60;
        private int oneHundredNanosecondsPerSecond = 10000000;
        private int defaultDwellTimeSeconds = 10;
        private const int maxEventDescriptors = 42; // Value determined by how many max length event descriptors (91 chars)
                                                    // stored as a JSON string can fit in 8K (max allowed for local settings)

        private CancellationTokenSource _cts = null;
        private IList<Geofence> geofences = new List<Geofence>();

        private DateTimeFormatter formatterShortDateLongTime;
        private DateTimeFormatter formatterLongTime;
        private Calendar calendar;
        private DecimalFormatter decimalFormatter;
        private CoreWindow coreWindow;

        // Push notification handling
        public PushNotificationChannel PushChannel;

        public MainPage()
        {
            this.InitializeComponent();
            try
            {
                formatterShortDateLongTime = new DateTimeFormatter("{month.integer}/{day.integer}/{year.full} {hour.integer}:{minute.integer(2)}:{second.integer(2)}", new[] { "en-US" }, "US", Windows.Globalization.CalendarIdentifiers.Gregorian, Windows.Globalization.ClockIdentifiers.TwentyFourHour);
                formatterLongTime = new DateTimeFormatter("{hour.integer}:{minute.integer(2)}:{second.integer(2)}", new[] { "en-US" }, "US", Windows.Globalization.CalendarIdentifiers.Gregorian, Windows.Globalization.ClockIdentifiers.TwentyFourHour);
                calendar = new Calendar();
                decimalFormatter = new DecimalFormatter();

                // Band setup
                PairBand();

                // Geofencing setup
                StartTracking();
                InitNotificationsAsync();

                coreWindow = CoreWindow.GetForCurrentThread(); // this needs to be set before InitializeComponent sets up event registration for app visibility
                coreWindow.VisibilityChanged += OnVisibilityChanged;
            }
            catch (Exception ex)
            {
                // GeofenceMonitor failed in adding a geofence
                // exceptions could be from out of memory, lat/long out of range,
                // too long a name, not a unique name, specifying an activation
                // time + duration that is still in the past
            }
        }

        private async void PairBand()
        {
            try
            {
                // Get the list of Microsoft Bands paired to the phone.
                IBandInfo[] pairedBands = await BandClientManager.Instance.GetBandsAsync();
                if (pairedBands.Length < 1)
                {
                    await new MessageDialog("No Paired Band.").ShowAsync();
                    return;
                }
                
                // Connect to Microsoft Band.
                bandClient = await BandClientManager.Instance.ConnectAsync(pairedBands[0]);

                // Create a Tile.
                myTileId = new Guid("D0BAB7A8-FFDC-43C3-B995-87AFB2A43387");
                BandTile myTile = new BandTile(myTileId)
                {
                    Name = "My Tile",
                    TileIcon = await LoadIcon("ms-appx:///Assets/TransparentLogo.png"),
                    SmallIcon = await LoadIcon("ms-appx:///Assets/TransparentLogoSmall.png")
                };

                // Remove the Tile from the Band, if present. An application won't need to do this everytime it runs. 
                // But in case you modify this sample code and run it again, let's make sure to start fresh.
                await bandClient.TileManager.RemoveTileAsync(myTileId);
                
                // Create the Tile on the Band.
                await bandClient.TileManager.AddTileAsync(myTile);

            }
            catch (Exception ex)
            {
                new MessageDialog(ex.ToString());
            }
        }

        private async Task<BandIcon> LoadIcon(string uri)
        {
            StorageFile imageFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(uri));

            using (IRandomAccessStream fileStream = await imageFile.OpenAsync(FileAccessMode.Read))
            {
                WriteableBitmap bitmap = new WriteableBitmap(1, 1);
                await bitmap.SetSourceAsync(fileStream);
                return bitmap.ToBandIcon();
            }
        }

        private async void StartTracking()
        {
            // Request permission to access location
            accessStatus = await Geolocator.RequestAccessAsync();

            switch (accessStatus)
            {
                case GeolocationAccessStatus.Allowed:
                    // You should set MovementThreshold for distance-based tracking
                    // or ReportInterval for periodic-based tracking before adding event
                    // handlers. If none is set, a ReportInterval of 1 second is used
                    // as a default and a position will be returned every 1 second.
                    //
                    // Value of 2000 milliseconds (2 seconds) 
                    // isn't a requirement, it is just an example.
                    _geolocator = new Geolocator { ReportInterval = 2000 };

                    // Subscribe to PositionChanged event to get updated tracking positions
                    _geolocator.PositionChanged += OnPositionChanged;

                    // Subscribe to StatusChanged event to get updates of location status changes
                    _geolocator.StatusChanged += OnStatusChanged;

                    await RefreshBreadcrumbs();

                    // Geofece setup
                    geofences = GeofenceMonitor.Current.Geofences;

                    // register for state change events
                    GeofenceMonitor.Current.GeofenceStateChanged += OnGeofenceStateChanged;
                    GeofenceMonitor.Current.StatusChanged += OnGeofenceStatusChanged;
                    break;

                case GeolocationAccessStatus.Denied:
                    break;

                case GeolocationAccessStatus.Unspecified:
                    break;
            }
        }
        private void StopTracking(object sender, RoutedEventArgs e)
        {
            _geolocator.PositionChanged -= OnPositionChanged;
            _geolocator.StatusChanged -= OnStatusChanged;
            _geolocator = null;
        }

        private async void InitNotificationsAsync()
        {
            Exception exception = null;

            try
            {
                var channel = await PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync();

                var hub = new NotificationHub("foodreconhub", "Endpoint=sb://foodreconhub-ns.servicebus.windows.net/;SharedAccessKeyName=DefaultListenSharedAccessSignature;SharedAccessKey=uh81JT3zDFcAganjZ3BL8FMRlKL1VkWUH6mX2ezTX6A=");
                var result = await hub.RegisterNativeAsync(channel.Uri);

                // Displays the registration ID so you know it was successful
                if (result.RegistrationId != null)
                {
                    //var dialog = new MessageDialog("Registration successful: " + result.RegistrationId);
                    //dialog.Commands.Add(new UICommand("OK"));
                    //await dialog.ShowAsync();
                    //UpdateStatus("Chat channel is ready.", false);

                    PushChannel = channel;
                    PushChannel.PushNotificationReceived += OnPushNotification;
                }
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            if (exception != null)
            {
                string msg1 = "An error has occurred while initializing cloud notifications." + Environment.NewLine + Environment.NewLine;

                // TO DO: Dissect the various potential errors and provide a more appropriate
                //        error message in msg2 for each of them.
                string msg2 = "Make sure that you have an active Internet connection and try again.";

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    new MessageDialog(msg1 + msg2, "Initialization Error");
                });
            }
        }

        private async void OnPushNotification(PushNotificationChannel sender, PushNotificationReceivedEventArgs e)
        {
            String notificationContent = String.Empty;

            e.Cancel = true;

            switch (e.NotificationType)
            {
                // Badges are not yet supported and will be added in a future version
                case PushNotificationType.Badge:
                    notificationContent = e.BadgeNotification.Content.GetXml();
                    break;

                // Tiles are not yet supported and will be added in a future version
                case PushNotificationType.Tile:
                    notificationContent = e.TileNotification.Content.GetXml();
                    break;

                // The current version of AzureChatr only works via toast notifications
                case PushNotificationType.Toast:
                    notificationContent = e.ToastNotification.Content.GetXml();
                    XmlDocument toastXml = e.ToastNotification.Content;

                    // Extract the relevant chat item data from the toast notification payload
                    XmlNodeList toastTextAttributes = toastXml.GetElementsByTagName("text");
                    string line1 = toastTextAttributes[0].InnerText;
                    string line2 = toastTextAttributes[1].InnerText;
                    string line3 = toastTextAttributes[2].InnerText;
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                    {
                        await RefreshBreadcrumbs();
                    });

                    break;

                // Raw notifications are not used in this version
                case PushNotificationType.Raw:
                    notificationContent = e.RawNotification.Content;
                    break;
            }
            //e.Cancel = true;
        }


        private async Task InsertBreadcrumb(Breadcrumbs breadcrumb)
        {
            // This code inserts a new Breadcrumb into the database. When the operation completes
            // and Mobile Services has assigned an Id, the item is added to the CollectionView
            await breadcrumbsTable.InsertAsync(breadcrumb);
            breadcrumbs.Add(breadcrumb);

            //await SyncAsync(); // offline sync
        }

        private async Task RefreshBreadcrumbs()
        {
            Exception exception = null;
            try
            {
                // This code refreshes the entries in the list view by querying the TodoItems table.
                // The query excludes completed TodoItems
                breadcrumbs = await breadcrumbsTable
                    .ToCollectionAsync();

                calendar.SetToNow();
                // Iterate through breadcrumbs and create geofence
                geofences.Clear();
                foreach (Breadcrumbs breadcrumb in breadcrumbs)
                {
                    Geofence geofence = GenerateGeofence(breadcrumb);
                    geofences.Add(geofence);
                    breadcrumb.Age = (calendar.GetDateTime().UtcDateTime - breadcrumb.StartTime).Minutes.ToString() + " m";
                }
            }
            catch (MobileServiceInvalidOperationException e)
            {
                exception = e;
            }
            catch (Exception e)
            {
                exception = e;
            }

            if (exception != null)
            {
                await new MessageDialog(exception.Message, "Error loading breadcrumbs").ShowAsync();
            }
            else
            {
                BreadcrumbsList.ItemsSource = breadcrumbs;
                this.ButtonNew.IsEnabled = true;
            }
        }

        private async Task UpdateCheckedBreadcrumb(Breadcrumbs item)
        {
            // This code takes a freshly completed Breadcrumb and updates the database. When the MobileService 
            // responds, the item is removed from the list 
            await breadcrumbsTable.UpdateAsync(item);
            breadcrumbs.Remove(item);
            BreadcrumbsList.Focus(Windows.UI.Xaml.FocusState.Unfocused);

            //await SyncAsync(); // offline sync
        }

        private async void ButtonSave_Click(object sender, RoutedEventArgs e)
        {
            calendar.SetToNow();
            String d = TextDescription.Text;
            if(d == "")
            {
                d = "Free Food!";
            }
            var Breadcrumb = new Breadcrumbs
            {
                Description = d,
                Latitude = lat,
                Longitude = lon,
                UpVotes = 1,
                DownVotes = 0,
                StartTime = calendar.GetDateTime().UtcDateTime
            };
            if (StandardPopup.IsOpen) { StandardPopup.IsOpen = false; }
            await InsertBreadcrumb(Breadcrumb);
        }

        // Handles the Click event on the Button inside the Popup control and 
        // closes the Popup. 
        private void ClosePopupClicked(object sender, RoutedEventArgs e)
        {
            // if the Popup is open, then close it 
            if (StandardPopup.IsOpen) { StandardPopup.IsOpen = false; }
        }

        // Handles the Click event on the Button on the page and opens the Popup. 
        private void ShowPopupOffsetClicked(object sender, RoutedEventArgs e)
        {
            // open the Popup if it isn't open already 
            if (!StandardPopup.IsOpen) { StandardPopup.IsOpen = true; }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            //await InitLocalStoreAsync(); // offline sync
            await RefreshBreadcrumbs();
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts = null;
            }

            GeofenceMonitor.Current.GeofenceStateChanged -= OnGeofenceStateChanged;
            GeofenceMonitor.Current.StatusChanged -= OnGeofenceStatusChanged;

            base.OnNavigatingFrom(e);
        }

        private void OnVisibilityChanged(CoreWindow sender, VisibilityChangedEventArgs args)
        {
            // NOTE: After the app is no longer visible on the screen and before the app is suspended
            // you might want your app to use toast notification for any geofence activity.
            // By registering for VisibiltyChanged the app is notified when the app is no longer visible in the foreground.

            if (args.Visible)
            {
                // register for foreground events
                GeofenceMonitor.Current.GeofenceStateChanged += OnGeofenceStateChanged;
                GeofenceMonitor.Current.StatusChanged += OnGeofenceStatusChanged;
            }
            else
            {
                // unregister foreground events (let background capture events)
                GeofenceMonitor.Current.GeofenceStateChanged -= OnGeofenceStateChanged;
                GeofenceMonitor.Current.StatusChanged -= OnGeofenceStatusChanged;
            }
        }

        async private void OnPositionChanged(Geolocator sender, PositionChangedEventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UpdateLocationData(e.Position);
            });
        }

        async private void OnStatusChanged(Geolocator sender, StatusChangedEventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                switch (e.Status)
                {
                    case PositionStatus.Ready:
                        // Location platform is providing valid data.
                        break;

                    case PositionStatus.Initializing:
                        // Location platform is attempting to acquire a fix. 
                        break;

                    case PositionStatus.NoData:
                        // Location platform could not obtain location data.
                        break;

                    case PositionStatus.Disabled:
                        // The permission to access location data is denied by the user or other policies.

                        // Show message to the user to go to location settings

                        // Clear cached location data if any
                        UpdateLocationData(null);
                        break;

                    case PositionStatus.NotInitialized:
                        // The location platform is not initialized. This indicates that the application 
                        // has not made a request for location data.
                        break;

                    case PositionStatus.NotAvailable:
                        // The location platform is not available on this version of the OS.
                        break;

                    default:
                        break;
                }
            });
        }

        private void UpdateLocationData(Geoposition position)
        {
            if (position == null)
            {

            }
            else
            {
                lat = position.Coordinate.Point.Position.Latitude;
                lon = position.Coordinate.Point.Position.Longitude;
            }
        }

        public async void OnGeofenceStatusChanged(GeofenceMonitor sender, object e)
        {
            var status = sender.Status;

            string eventDescription = GetTimeStampedMessage("Geofence Status Changed");
            eventDescription += " (" + status.ToString() + ")";

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
            });
        }

        public async void OnGeofenceStateChanged(GeofenceMonitor sender, object e)
        {
            var reports = sender.ReadReports();

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                foreach (GeofenceStateChangeReport report in reports)
                {
                    GeofenceState state = report.NewState;
                    Geofence geofence = report.Geofence;
                    string eventDescription = GetTimeStampedMessage(geofence.Id);

                    if (state == GeofenceState.Removed)
                    {

                        // remove the geofence from the client side geofences collection
                        Remove(geofence);
                    }
                    else if (state == GeofenceState.Entered)
                    {
                        // Send a notification.
                        await bandClient.NotificationManager.SendMessageAsync(myTileId, "FoodRecon", "Free Food Sighted!", DateTimeOffset.Now, MessageFlags.ShowDialog);
                    }
                    else if (state == GeofenceState.Exited)
                    {

                    }
                }
            });
        }

        /// <summary>
        /// This method removes the geofence from the client side geofences collection
        /// </summary>
        /// <param name="geofence"></param>
        private void Remove(Geofence geofence)
        {
            try
            {
                if (!geofences.Remove(geofence))
                {
                    var strMsg = "Could not find Geofence " + geofence.Id + " in the geofences collection";
                }
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// This is the click handler for the 'Remove Geofence Item' button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnRemoveGeofenceItem(object sender, RoutedEventArgs e)
        {
            //if (null != RegisteredGeofenceListBox.SelectedItem)
            {
                // get selected item
                GeofenceItem itemToRemove = null; //RegisteredGeofenceListBox.SelectedItem as GeofenceItem;

                var geofence = itemToRemove.Geofence;

                // remove the geofence from the client side geofences collection
                Remove(geofence);
            }
        }

        private Geofence GenerateGeofence(Breadcrumbs breadcrumb)
        {
            string fenceKey = new string(breadcrumb.Id.ToCharArray());

            BasicGeoposition position;
            position.Latitude = breadcrumb.Latitude;
            position.Longitude = breadcrumb.Longitude;
            position.Altitude = 0.0;
            double radius = 50;

            // the geofence is a circular region
            Geocircle geocircle = new Geocircle(position, radius);

            bool singleUse = false;

            // want to listen for enter geofence, exit geofence and remove geofence events
            // you can select a subset of these event states
            MonitoredGeofenceStates mask = MonitoredGeofenceStates.Entered | MonitoredGeofenceStates.Exited | MonitoredGeofenceStates.Removed;

            TimeSpan dwellTime;
            TimeSpan duration;
            DateTimeOffset startTime;

            try
            {
                // setting up how long you need to be in geofence for enter event to fire
                dwellTime = new TimeSpan(ParseTimeSpan("0", defaultDwellTimeSeconds));

                // setting up how long the geofence should be active
                duration = new TimeSpan(ParseTimeSpan("0", 0));

                // setting up the start time of the geofence
                calendar.SetToNow();
                startTime = calendar.GetDateTime();
            }
            catch (ArgumentNullException)
            {
            }
            catch (FormatException)
            {
            }
            catch (ArgumentException)
            {
            }

            return new Geofence(fenceKey, geocircle, mask, singleUse, dwellTime, startTime, duration);
        }

        #region Offline sync
        //private async Task InitLocalStoreAsync()
        //{
        //    if (!App.MobileService.SyncContext.IsInitialized)
        //    {
        //        var store = new MobileServiceSQLiteStore("localstore.db");
        //        store.DefineTable<Breadcrumbs>();
        //        await App.MobileService.SyncContext.InitializeAsync(store);
        //    }
        //
        //    await SyncAsync();
        //}

        //private async Task SyncAsync()
        //{
        //    await App.MobileService.SyncContext.PushAsync();
        //    await breadcrumbsTable.PullAsync("todoItems", breadcrumbsTable.CreateQuery());
        //}

        #endregion
    }
}
