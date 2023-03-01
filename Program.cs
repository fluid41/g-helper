using Microsoft.Win32;
using System.Diagnostics;
using System.Management;
using System.Text.Json;

public class AppConfig
{

    string appPath;
    string configFile;

    public Dictionary<string, object> config = new Dictionary<string, object>();

    public AppConfig()
    {

        appPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\GHelper";
        configFile = appPath + "\\config.json";

        if (!System.IO.Directory.Exists(appPath))
            System.IO.Directory.CreateDirectory(appPath);

        if (File.Exists(configFile))
        {
            string text = File.ReadAllText(configFile);
            try
            {
                config = JsonSerializer.Deserialize<Dictionary<string, object>>(text);
            }
            catch
            {
                initConfig();
            }
        }
        else
        {
            initConfig();
        }

    }

    private void initConfig()
    {
        config = new Dictionary<string, object>();
        config["performance_mode"] = 0;
        string jsonString = JsonSerializer.Serialize(config);
        File.WriteAllText(configFile, jsonString);
    }

    public int getConfig(string name)
    {
        if (config.ContainsKey(name))
            return int.Parse(config[name].ToString());
        else return -1;
    }

    public string getConfigString(string name)
    {
        if (config.ContainsKey(name))
            return config[name].ToString();
        else return null;
    }

    public void setConfig(string name, int value)
    {
        config[name] = value;
        string jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configFile, jsonString);
    }

    public void setConfig(string name, string value)
    {
        config[name] = value;
        string jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configFile, jsonString);
    }

    public string getFanName(int device)
    {
        int mode = getConfig("performance_mode");
        string name;

        if (device == 1)
            name = "gpu";
        else
            name = "cpu";

        return "fan_profile_" + name + "_" + mode;
    }

    public byte[] getFanConfig(int device)
    {
        string curveString = getConfigString(getFanName(device));
        byte[] curve = { };

        if (curveString is not null)
            curve = StringToBytes(curveString);

        return curve;
    }

    public void setFanConfig(int device, byte[] curve)
    {
        string bitCurve = BitConverter.ToString(curve);
        setConfig(getFanName(device), bitCurve);
    }


    public static byte[] StringToBytes(string str)
    {
        String[] arr = str.Split('-');
        byte[] array = new byte[arr.Length];
        for (int i = 0; i < arr.Length; i++) array[i] = Convert.ToByte(arr[i], 16);
        return array;
    }

    public byte[] getDefaultCurve(int device)
    {
        int mode = getConfig("performance_mode");
        byte[] curve;

        switch (mode)
        {
            case 1:
                if (device == 1)
                    curve = StringToBytes("14-3F-44-48-4C-50-54-62-16-1F-26-2D-39-47-55-5F");
                else
                    curve = StringToBytes("14-3F-44-48-4C-50-54-62-11-1A-22-29-34-43-51-5A");
                break;
            case 2:
                if (device == 1)
                    curve = StringToBytes("3C-41-42-46-47-4B-4C-62-08-11-11-1D-1D-26-26-2D");
                else
                    curve = StringToBytes("3C-41-42-46-47-4B-4C-62-03-0C-0C-16-16-22-22-29");
                break;
            default:
                if (device == 1)
                    curve = StringToBytes("3A-3D-40-44-48-4D-51-62-0C-16-1D-1F-26-2D-34-4A");
                else
                    curve = StringToBytes("3A-3D-40-44-48-4D-51-62-08-11-16-1A-22-29-30-45");
                break;
        }

        return curve;
    }

}

public class HardwareMonitor
{

    public static float? cpuTemp = -1;
    public static float? batteryDischarge = -1;


    public static void ReadSensors()
    {
        cpuTemp = -1;
        batteryDischarge = -1;

        try
        {
            var ct = new PerformanceCounter("Thermal Zone Information", "Temperature", @"\_TZ.THRM", true);
            cpuTemp = ct.NextValue() - 273;
            ct.Dispose();

            var cb = new PerformanceCounter("Power Meter", "Power", "Power Meter (0)", true);
            batteryDischarge = cb.NextValue() / 1000;
            cb.Dispose();
        }
        catch
        {
            Debug.WriteLine("Failed reading sensors");
        }
    }

}

namespace GHelper
{
    static class Program
    {
        public static NotifyIcon trayIcon = new NotifyIcon
        {
            Text = "G-Helper",
            Icon = Properties.Resources.standard,
            Visible = true
        };

        public static ASUSWmi wmi = new ASUSWmi();
        public static AppConfig config = new AppConfig();

        public static SettingsForm settingsForm = new SettingsForm();
        public static ToastForm toast = new ToastForm();

        // The main entry point for the application
        public static void Main()
        {

            trayIcon.MouseClick += TrayIcon_MouseClick; ;

            wmi.SubscribeToEvents(WatcherEventArrived);

            settingsForm.InitGPUMode();
            settingsForm.InitBoost();
            settingsForm.InitAura();

            settingsForm.SetPerformanceMode(config.getConfig("performance_mode"));
            settingsForm.SetBatteryChargeLimit(config.getConfig("charge_limit"));

            settingsForm.VisualiseGPUAuto(config.getConfig("gpu_auto"));
            settingsForm.VisualiseScreenAuto(config.getConfig("screen_auto"));

            settingsForm.SetStartupCheck(Startup.IsScheduled());

            bool isPlugged = (System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online);
            settingsForm.AutoGPUMode(isPlugged ? 1 : 0);
            settingsForm.AutoScreen(isPlugged ? 1 : 0);

            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;

            IntPtr ds = settingsForm.Handle;

            Application.Run();

        }

        private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            bool isPlugged = (System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online);
            settingsForm.AutoGPUMode(isPlugged ? 1 : 0);
            settingsForm.AutoScreen(isPlugged ? 1 : 0);

            settingsForm.SetBatteryChargeLimit(config.getConfig("charge_limit"));
        }


        static void LaunchProcess(string fileName = "")
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = fileName;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            start.CreateNoWindow = true;
            try
            {
                Process proc = Process.Start(start);
            }
            catch
            {
                Debug.WriteLine("Failed to run " + fileName);
            }


        }

        static void WatcherEventArrived(object sender, EventArrivedEventArgs e)
        {
            var collection = (ManagementEventWatcher)sender;

            if (e.NewEvent is null) return;

            int EventID = int.Parse(e.NewEvent["EventID"].ToString());

            Debug.WriteLine(EventID);

            switch (EventID)
            {
                case 124:    // M3
                    switch (config.getConfig("m3"))
                    {
                        case 1:
                            NativeMethods.KeyPress(NativeMethods.VK_MEDIA_PLAY_PAUSE);
                            break;
                        case 2:
                            settingsForm.BeginInvoke(settingsForm.CycleAuraMode);
                            break;
                        case 3:
                            LaunchProcess(config.getConfigString("m3_custom"));
                            break;
                        default:
                            NativeMethods.KeyPress(NativeMethods.VK_VOLUME_MUTE);
                            break;
                    }
                    return;
                case 56:    // M4 / Rog button
                    switch (config.getConfig("m4"))
                    {
                        case 1:
                            settingsForm.BeginInvoke(SettingsToggle);
                            break;
                        case 2:
                            LaunchProcess(config.getConfigString("m4_custom"));
                            break;
                        default:
                            settingsForm.BeginInvoke(settingsForm.CyclePerformanceMode);
                            break;
                    }
                    return;
                case 174:   // FN+F5
                    settingsForm.BeginInvoke(settingsForm.CyclePerformanceMode);
                    return;
                case 179:   // FN+F4
                    settingsForm.BeginInvoke(delegate
                    {
                        settingsForm.CycleAuraMode();
                    });
                    return;
                case 87:  // Battery
                    /*
                    settingsForm.BeginInvoke(delegate
                    {
                        settingsForm.AutoGPUMode(0);
                        settingsForm.AutoScreen(0);
                    });
                    */
                    return;
                case 88:  // Plugged
                    /*
                    settingsForm.BeginInvoke(delegate
                    {
                        settingsForm.AutoScreen(1);
                        settingsForm.AutoGPUMode(1);
                    });
                    */
                    return;

            }


        }

        static void SettingsToggle()
        {
            if (settingsForm.Visible)
                settingsForm.Hide();
            else
            {
                settingsForm.Show();
                settingsForm.Activate();
            }

            settingsForm.VisualiseGPUMode();

        }

        static void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                SettingsToggle();
            }
        }



        static void OnExit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            Application.Exit();
        }
    }

}