﻿using Acr.UserDialogs;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Usb;
using Android.OS;
using Android.Util;
using Android.Views;
using Mono.Unix;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android;
using Android.Util;
using Android.Bluetooth;
using Android.Runtime;
using AndroidX.Core.App;
using Android.Bluetooth;
using AndroidX.Core.Content;
using Xamarin.Essentials;
using MissionPlanner.GCSViews;
using MissionPlanner.GCSViews.ConfigurationView;
using Environment = Android.OS.Environment;
using Settings = MissionPlanner.Utilities.Settings;
using Thread = System.Threading.Thread;
using Android.Content;
using Android.Provider;
using Android.Views.InputMethods;
using Android.Widget;
using Hoho.Android.UsbSerial.Util;
using Java.Lang;
using MissionPlanner.Comms;
using MissionPlanner.Utilities;
using Xamarin.Forms;
using Xamarin.GCSViews;
using Application = Android.App.Application;
using Exception = System.Exception;
using File = Java.IO.File;
using Process = Android.OS.Process;
using String = System.String;
using Toolbar = AndroidX.AppCompat.Widget.Toolbar;
using Uri = Android.Net.Uri;
using View = Android.Views.View;

[assembly: UsesFeature("android.hardware.usb.host", Required = false)]
[assembly: UsesFeature("android.hardware.bluetooth", Required = false)]
[assembly: UsesLibrary("org.apache.http.legacy", false)]
[assembly: UsesPermission("android.permission.RECEIVE_D2D_COMMANDS")]
//[assembly: UsesPermission("android.permission.MANAGE_EXTERNAL_STORAGE")]

namespace Xamarin.Droid
{ //global::Android.Content.Intent.CategoryLauncher
  //global::Android.Content.Intent.CategoryHome,
    [IntentFilter(new[] { global::Android.Content.Intent.ActionMain, global::Android.Content.Intent.ActionAirplaneModeChanged , 
        global::Android.Content.Intent.ActionBootCompleted , UsbManager.ActionUsbDeviceAttached, UsbManager.ActionUsbDeviceDetached, 
        global::Android.Bluetooth.BluetoothDevice.ActionFound, global::Android.Bluetooth.BluetoothDevice.ActionAclConnected, UsbManager.ActionUsbAccessoryAttached}, 
        Categories = new []{ global::Android.Content.Intent.CategoryLauncher})]
    [IntentFilter(actions: new[] { global::Android.Content.Intent.ActionView }, Categories = new[] { global::Android.Content.Intent.CategoryBrowsable, global::Android.Content.Intent.ActionDefault, global::Android.Content.Intent.CategoryOpenable }, DataHost = "*", DataPathPattern = ".*\\.tlog", DataMimeType = "*/*", DataSchemes = new[] { "file", "http", "https", "content" })]
    [IntentFilter(actions: new[] { global::Android.Content.Intent.ActionView }, Categories = new[] { global::Android.Content.Intent.CategoryBrowsable, global::Android.Content.Intent.ActionDefault, global::Android.Content.Intent.CategoryOpenable }, DataHost = "*", DataPathPattern = ".*\\.bin", DataMimeType = "*/*", DataSchemes = new[] { "file", "http", "https", "content" })] 
    [MetaData("android.hardware.usb.action.USB_DEVICE_ATTACHED", Resource = "@xml/device_filter")]
    [Activity(Label = "Mission Planner", ScreenOrientation = ScreenOrientation.SensorLandscape, Icon = "@mipmap/icon", Theme = "@style/MainTheme", 
        MainLauncher = true, HardwareAccelerated = true, DirectBootAware = true, Immersive = true, LaunchMode = LaunchMode.SingleInstance)]
    public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity
    {
        private const int SAF = 12321;
        readonly string TAG = "MP";
        private Socket server;
        public UsbDeviceReceiver UsbBroadcastReceiver;

        public static MainActivity Current { private set; get; }
        public static readonly int PickImageId = 1000;
        private DeviceDiscoveredReceiver BTBroadcastReceiver;
        private AndroidVideo androidvideo;

        public TaskCompletionSource<string> PickImageTaskCompletionSource { set; get; }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == PickImageId)
            {
                if ((resultCode == Result.Ok) && (data != null))
                {
                    // Set the filename as the completion of the Task
                    PickImageTaskCompletionSource.SetResult(data.DataString);
                }
                else
                {
                    PickImageTaskCompletionSource.SetResult(null);
                }
            }

            if (requestCode == SAF)
            {
                // content:/com.android.externalstorage.documents/tree/primary%3AMp

                var pref = this.GetSharedPreferences("pref", FileCreationMode.Private);

                Uri docUriTree =
                    DocumentsContract.BuildDocumentUriUsingTree(data.Data,
                        DocumentsContract.GetTreeDocumentId(data.Data));

                var query = this.ContentResolver.Query(docUriTree, null, null,
                    null, null);
                query.MoveToFirst();
                var filePath = query.GetString(0); 
                query.Close();

                pref.Edit().PutString("Directory", filePath).Commit();

                ContinueInit();
            }
        }

        public static void ShowKeyboard(View pView) {
            pView.RequestFocus();

            InputMethodManager inputMethodManager = Current.GetSystemService(Context.InputMethodService) as InputMethodManager;
            inputMethodManager.ShowSoftInput(pView, ShowFlags.Forced);
            inputMethodManager.ToggleSoftInput(ShowFlags.Forced, HideSoftInputFlags.ImplicitOnly);
        }

        public static void HideKeyboard(View pView) {
            InputMethodManager inputMethodManager = Current.GetSystemService(Context.InputMethodService) as InputMethodManager;
            inputMethodManager.HideSoftInputFromWindow(pView.WindowToken, HideSoftInputFlags.None);
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            Current = this;

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;

            TabLayoutResource = Resource.Layout.Tabbar;
            ToolbarResource = Resource.Layout.Toolbar;

            SetSupportActionBar((Toolbar) FindViewById(ToolbarResource));

            this.Window.AddFlags(WindowManagerFlags.Fullscreen | WindowManagerFlags.TurnScreenOn |
                                 WindowManagerFlags.HardwareAccelerated);

            base.OnCreate(savedInstanceState);

            global::Xamarin.Forms.Forms.Init(this, savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);

            var pref = this.GetSharedPreferences("pref", FileCreationMode.Private);

            var pass = false;

            if (pref.Contains("Directory"))
            {
                try
                {
                    var files = Directory.GetFiles(pref.GetString("Directory", ""), "*.*");
                    pass = true;
                }
                catch
                {
                    pass = false;
                }
            }
            else
            {
                pass = false;
            }
            /*
            if (!pass)
            {
                Intent intent = new Intent(Intent.ActionOpenDocumentTree);
                intent.AddCategory(Intent.CategoryDefault);
                intent.AddFlags(ActivityFlags.GrantPersistableUriPermission);

                //intent.PutExtra(DocumentsContract.ExtraInitialUri, Application.Context.getExternalStorageDirectory "MissionPlanner");
                StartActivityForResult(Intent.CreateChooser(intent, "Select a folder to save config settings"), SAF);
            }
            else*/
            {
                ContinueInit();
            }
        }

        void ContinueInit()
        {

            var list = Application.Context.GetExternalFilesDirs(null);
            list.ForEach(a => Log.Info("MP", "External dir option: " + a.AbsolutePath));

            var list2 = Application.Context.GetExternalFilesDirs(Environment.DirectoryDownloads);
            list2.ForEach(a => Log.Info("MP", "External DirectoryDownloads option: " + a.AbsolutePath));

            var pref = this.GetSharedPreferences("pref", FileCreationMode.Private);


            Settings.CustomUserDataDirectory = Application.Context.GetExternalFilesDir(null).ToString();
                //pref.GetString("Directory", Application.Context.GetExternalFilesDir(null).ToString());
            Log.Info("MP", "Settings.CustomUserDataDirectory " + Settings.CustomUserDataDirectory);

            try { 
                WinForms.BundledPath = Application.Context.ApplicationInfo.NativeLibraryDir;
                GStreamer.BundledPath = Application.Context.ApplicationInfo.NativeLibraryDir;
            } catch { }
            Log.Info("MP", "WinForms.BundledPath " + WinForms.BundledPath);

            try
            {
                JavaSystem.LoadLibrary("gstreamer_android");

                Org.Freedesktop.Gstreamer.GStreamer.Init(this.ApplicationContext);
            }
            catch (Exception ex) { Log.Error("MP", ex.ToString()); }

            Test.BlueToothDevice = new BTDevice();
            Test.UsbDevices = new USBDevices();
            Test.Radio = new Radio();
            Test.GPS = new GPS();
            Test.SystemInfo = new SystemInfo();

            androidvideo = new AndroidVideo();
            //disable
            //androidvideo.Start();
            AndroidVideo.onNewImage += (e, o) => 
            {
                WinForms.SetHUDbg(o);
            };
            

            //ConfigFirmwareManifest.ExtraDeviceInfo
            /*
            var intent = new global::Android.Content.Intent(Intent.ActionOpenDocumentTree);

            intent.AddFlags(ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);
            intent.PutExtra(DocumentsContract.ExtraInitialUri, "Mission Planner");

            StartActivityForResult(intent, 1);
            */

            UserDialogs.Init(this);

            AndroidEnvironment.UnhandledExceptionRaiser += AndroidEnvironment_UnhandledExceptionRaiser;

            {
                if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.AccessFineLocation) !=
                    (int) Permission.Granted ||
                    ContextCompat.CheckSelfPermission(this, Manifest.Permission.WriteExternalStorage) !=
                    (int) Permission.Granted ||
                    ContextCompat.CheckSelfPermission(this, Manifest.Permission.Bluetooth) !=
                    (int) Permission.Granted)
                {
                    ActivityCompat.RequestPermissions(this,
                        new String[]
                        {
                            Manifest.Permission.AccessFineLocation, Manifest.Permission.LocationHardware,
                            Manifest.Permission.WriteExternalStorage, Manifest.Permission.ReadExternalStorage,
                            Manifest.Permission.Bluetooth
                        }, 1);
                }

                while (ContextCompat.CheckSelfPermission(this, Manifest.Permission.WriteExternalStorage) !=
                       (int) Permission.Granted)
                {
                    Thread.Sleep(1000);
                    var text = "Checking Permissions - " + DateTime.Now.ToString("T");

                    DoToastMessage(text);
                }
            }

            try {
                // print some info
                var pm = this.PackageManager;
                var name = this.PackageName;

                var pi = pm.GetPackageInfo(name, PackageInfoFlags.Activities);

                Console.WriteLine("pi.ApplicationInfo.DataDir " + pi?.ApplicationInfo?.DataDir);
                Console.WriteLine("pi.ApplicationInfo.NativeLibraryDir " + pi?.ApplicationInfo?.NativeLibraryDir);

                // api level 24 - android 7
                Console.WriteLine("pi.ApplicationInfo.DeviceProtectedDataDir " +
                                  pi?.ApplicationInfo?.DeviceProtectedDataDir);
            } catch {}


            {
                // clean start, see if it was an intent/usb attach
                //if (savedInstanceState == null)
                {
                    DoToastMessage("Init Saved State");
                    proxyIfUsbAttached(this.Intent);

                    Console.WriteLine(this.Intent?.Action);
                    Console.WriteLine(this.Intent?.Categories);
                    Console.WriteLine(this.Intent?.Data);
                    Console.WriteLine(this.Intent?.DataString);
                    Console.WriteLine(this.Intent?.Type);
                }
            }

            GC.Collect();
            /*
            Task.Run(() =>
            {
                var gdaldir = Settings.GetRunningDirectory() + "gdalimages";
                Directory.CreateDirectory(gdaldir);

                MissionPlanner.Utilities.GDAL.GDALBase = new GDAL.GDAL();

                GDAL.GDAL.ScanDirectory(gdaldir);

                GMap.NET.MapProviders.GMapProviders.List.Add(GDAL.GDALProvider.Instance);
            });
            */

            DoToastMessage("Launch App");

            LoadApplication(new App());
        }

        public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent e)
        {
            Log.Debug(TAG, "OnKeyDown " + keyCode);
            switch (keyCode)
            {
                case Keycode.VolumeUp:
                    Toast.MakeText(this, "VolumeUp key pressed", ToastLength.Short).Show();
                    e.StartTracking();
                    return true;
                case Keycode.ButtonL1:
                    e.StartTracking();
                    return true;
                case Keycode.ButtonL2:
                    e.StartTracking();
                    return true;
                case Keycode.ButtonR1:
                    e.StartTracking();
                    return true;
                case Keycode.ButtonR2:
                    e.StartTracking();
                    return true;  
                case Keycode.ButtonMode:
                    e.StartTracking();
                    return true;
                case Keycode.ButtonSelect:
                    e.StartTracking();
                    return true;
            }

            return base.OnKeyDown(keyCode, e);
        }

        public override bool OnKeyUp([GeneratedEnum] Keycode keyCode, KeyEvent e)
        {
            Log.Debug(TAG, "OnKeyUp " + keyCode);

            if ((e.Flags & KeyEventFlags.CanceledLongPress) == 0)
            {
                if (keyCode == Keycode.VolumeUp)
                {
                    Log.Error(TAG, "Short press KEYCODE_VOLUME_UP");
                    return true;
                }
                else if (keyCode == Keycode.VolumeDown)
                {
                    Log.Error(TAG, "Short press KEYCODE_VOLUME_DOWN");
                    return true;
                }
            }

            return base.OnKeyUp(keyCode, e);
        }

        public override bool OnKeyLongPress([GeneratedEnum] Keycode keyCode, KeyEvent e)
        {
            Log.Debug(TAG, "OnKeyLongPress " + keyCode);

            if (keyCode == Keycode.VolumeUp)
            {
                Log.Debug(TAG, "Long press KEYCODE_VOLUME_UP");
                return true;
            }
            else if (keyCode == Keycode.VolumeDown)
            {
                Log.Debug(TAG, "Long press KEYCODE_VOLUME_DOWN");
                return true;
            }

            return base.OnKeyLongPress(keyCode, e);
        }

        private void CurrentDomain_FirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            Log.Error(TAG, e.Exception.ToString());
            Debugger.Break();
        }

        private void DoToastMessage(string text, ToastLength toastLength = ToastLength.Short)
        {
            try
            {
                // thread to force invoke into ui thread
                Task.Run(() =>
                {
                    if (!this.IsFinishing)
                    {
                        //if (Looper.MainLooper.IsCurrentThread)
                        {
                            // On UI thread.
                            RunOnUiThread(() =>
                            {
                                try
                                {
                                    Toast toast = Toast.MakeText(this, text, toastLength);
                                    toast.Show();
                                }
                                catch
                                {

                                }
                            });
                        }
                    }
                });
            } catch {}
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            Console.WriteLine("OnNewIntent " + intent.Action);
        }

        private void proxyIfUsbAttached(Intent intent) {

            if (intent == null) return;

            if (!UsbManager.ActionUsbDeviceAttached.Equals(intent.Action)) return;

            Log.Verbose(TAG, "usb device attached");

            WinForms.InitDevice = ()=>
            {
                Log.Info(TAG, "WinForms.InitDevice");
                UsbBroadcastReceiver.OnReceive(this.ApplicationContext, intent);
            };
        }

        protected override void OnStart()
        {
            base.OnStart();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        public async Task<PermissionStatus> CheckAndRequestPermissionAsync<T>(T permission)
            where T : Permissions.BasePermission
        {
            Console.WriteLine("Check Perm " + permission.ToString());
            var status = await permission.CheckStatusAsync();
            if (status != PermissionStatus.Granted)
            {
                Console.WriteLine("Request Perm " + permission.ToString());
                status = await permission.RequestAsync();
            }

            Console.WriteLine("Status Perm " + permission.ToString() + " " + status);
            return status;
        }

        private async Task CheckPerm()
        {
            await CheckAndRequestPermissionAsync((new Permissions.LocationWhenInUse()));
            await CheckAndRequestPermissionAsync((new Permissions.StorageWrite()));
        }

        private void AndroidEnvironment_UnhandledExceptionRaiser(object sender, RaiseThrowableEventArgs e)
        {
            Log.Error("MP", e.Exception.StackTrace.ToString());
            Debugger.Break();
            e.Handled = true;
            DoToastMessage("ERROR " + e.Exception.Message, ToastLength.Long);
            throw e.Exception;
        }

        protected override void OnResume()
        {
            base.OnResume();

            this.Window.DecorView.SystemUiVisibility =
                (StatusBarVisibility) (SystemUiFlags.LowProfile
                                       | SystemUiFlags.Fullscreen
                                       | SystemUiFlags.HideNavigation
                                       | SystemUiFlags.Immersive
                                       | SystemUiFlags.ImmersiveSticky);

            StartD2DInfo();

            //register the broadcast receivers
            UsbBroadcastReceiver = new UsbDeviceReceiver(this);
            RegisterReceiver(UsbBroadcastReceiver, new IntentFilter(UsbManager.ActionUsbDeviceDetached));
            RegisterReceiver(UsbBroadcastReceiver, new IntentFilter(UsbManager.ActionUsbDeviceAttached));

            // Register for broadcasts when a device is discovered
            BTBroadcastReceiver = new DeviceDiscoveredReceiver(this);
            RegisterReceiver(BTBroadcastReceiver, new IntentFilter(BluetoothDevice.ActionFound));
            RegisterReceiver(BTBroadcastReceiver, new IntentFilter(BluetoothAdapter.ActionDiscoveryFinished));
        }

        protected override void OnPause()
        {
            base.OnPause();

            StopD2DInfo();

            UnregisterReceiver(UsbBroadcastReceiver);

            UnregisterReceiver(BTBroadcastReceiver);
        }

        public void StopD2DInfo()
        {
            server.Close();
            server = null;
        }

        public void StartD2DInfo()
        {
            {
                try
                {
                    //var d2dinfo = new UnixEndPoint("/tmp/d2dinfo");
                    //var d2dinfo = "songdebugmessage";
                    var d2dinfo = "linkstate";
                    //"d2dsignal";

                    server = new Socket(AddressFamily.Unix, SocketType.Stream, 0);
                    server.Bind(new AbstractUnixEndPoint(d2dinfo));

                    server.Listen(50);

                    Task.Run(() =>
                    {
                        while (server != null)
                        {
                            try
                            {
                                var socket = server.Accept();
                                Thread.Sleep(1);
                                byte[] buffer = new byte[100];
                                var readlen = 0;
                                do
                                {
                                    readlen = socket.Receive(buffer);
                                    if ((readlen > 4) && (readlen >= (4 + buffer[3])))
                                    {
                                        //Log.Info(TAG, "Got " + ASCIIEncoding.ASCII.GetString(buffer, 4, buffer[3]));
                                    }
                                } while (readlen > 0);
                                socket.Close();

                            }
                            catch (Exception ex) { Log.Warn(TAG, ex.ToString()); Thread.Sleep(1000); }
                        }
                    });

                }
                catch (Exception ex) { Log.Warn(TAG, ex.ToString()); }
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error(TAG, e.ExceptionObject.ToString());
            Debugger.Break();
        }

    }

    public class GPS : IGPS
    {
        public Task<(double lat, double lng, double alt)> GetPosition()
        {
            return Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Best)).ContinueWith<(double,double,double)>(
                location =>
                {
                    return (location.Result.Latitude, location.Result.Longitude,
                        location.Result.Altitude.HasValue ? location.Result.Altitude.Value : 0.0);
                }
            );
        }
    }

    public class SystemInfo : ISystemInfo
    {
        public string GetSystemTag()
        {
            // android version
            try
            {
                return SysProp.GetProp("ro.build.fingerprint");
            }
            catch
            {
                return "";
            }
        }
    }
}