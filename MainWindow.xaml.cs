/*
  The MIT License
  Copyright © 2020 Arnaldo Baumanis

  Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
  associated documentation files (the “Software”), to deal in the Software without restriction,
  including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
  and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
  subject to the following conditions:
  
  The above copyright notice and this permission notice shall be included in all copies or substantial
  portions of the Software.
  
  THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
  LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
  IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
  CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Configuration; // config file
using System.Collections.Specialized; // config file
using System.Windows;
using System.Windows.Input;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using NvAPIWrapper.GPU;
//using Microsoft.Win32.TaskScheduler;

namespace Coolest_Control_Center
{
  public partial class MainWindow : Window
  {
    Thread ThreadMemory;
    Thread ThreadDrives;
    Thread ThreadCpuUsage;
    Thread ThreadGpuInformation;
    Thread ThreadBattery;
    readonly System.Windows.Threading.DispatcherTimer dispatcherTimer = new System.Windows.Threading.DispatcherTimer();

    private NotifyIcon notifyIcon = null;

    //public readonly string root = Environment.GetEnvironmentVariable("SystemRoot");
    //public readonly string CurrentDirectory = Environment.CurrentDirectory;
    public readonly string AppName = AppDomain.CurrentDomain.FriendlyName;
    public readonly string AppVersion = System.Windows.Forms.Application.ProductVersion;

    // WMI querys
    private readonly ManagementObjectSearcher CPUObject = new ManagementObjectSearcher("select Name, MaxClockSpeed, CurrentClockSpeed from Win32_Processor");
    //private readonly ManagementObjectSearcher GPUObject = new ManagementObjectSearcher("select Name, AdapterRAM from Win32_VideoController");
    private readonly ManagementObjectSearcher RAMObject = new ManagementObjectSearcher("select * from Win32_OperatingSystem");
    private readonly ManagementObjectSearcher ComputerSystemObject = new ManagementObjectSearcher("select Model FROM Win32_ComputerSystem");
    private readonly ManagementObjectSearcher PowerPlanObject = new ManagementObjectSearcher(@"root\cimv2\power", "select * FROM Win32_PowerPlan");
    private readonly ManagementObjectSearcher AsusAtkObject = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AsusAtkWmi_WMNB");
    //private readonly ManagementObjectSearcher AsusEventObject = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AsusAtkWmiEvent");
    //private readonly ManagementObjectSearcher CPUTempObject = new ManagementObjectSearcher(@"root\WMI", "Select * From MSAcpi_ThermalZoneTemperature");

    private readonly PerformanceCounter CpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
    private readonly PerformanceCounter CpuTemperature = new PerformanceCounter("Thermal Zone Information", "Temperature", @"\_TZ.THRM");
    private readonly PerformanceCounter CpuFrequency = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");

    readonly string[] MachinesSupported = {
      "rog zephyrus g14 ga401ih_ga401ih",
      "rog zephyrus g14 ga401ih_ga401iv",
      "rog zephyrus g14 ga401ih_ga401iu",
    };

    public MainWindow()
    {
      //WindowsIdentity identity = WindowsIdentity.GetCurrent();
      //WindowsPrincipal principal = new WindowsPrincipal(identity);
      //if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
      //{
      //  System.Windows.MessageBox.Show("This program must be run as an administrator!", AppName, (MessageBoxButton)MessageBoxButtons.OK);
      //  var exeName = Process.GetCurrentProcess().MainModule.FileName;
      //  ProcessStartInfo startInfo = new ProcessStartInfo(exeName);
      //  startInfo.Verb = "runas";
      //  startInfo.Arguments = "restart";
      //  Process.Start(startInfo);
      //  System.Windows.Application.Current.Shutdown();
      //}

      if (Environment.OSVersion.Platform != PlatformID.Win32NT) System.Windows.Application.Current.Shutdown();
      if (Environment.Is64BitOperatingSystem == false) System.Windows.Application.Current.Shutdown();

      InitializeComponent();
      
      if (!MachinesSupported.Contains(GetMachineVendor().Trim().ToLower()))
      {
        System.Windows.MessageBox.Show("Sorry, your computer is not supported", AppName, (MessageBoxButton)MessageBoxButtons.OK);
        System.Windows.Application.Current.Shutdown();
      }

      LoadConfigFile();

      // Provides access to power system notifications
      SystemEvents.PowerModeChanged += PowerModeChanged;

      dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
      dispatcherTimer.Interval = new TimeSpan(0, 0, 2);
    }

    #region Convert bytes in MB, GB, TB
    /// <summary>
    /// Convert bytes in MB, GB, TB, PB, EB, ZB, YB
    /// </summary>
    static readonly string[] SizeSuffixes = { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

    static string SizeSuffix(Int64 value)
    {
      if (value < 0) { return "-" + SizeSuffix(-value); }
      if (value == 0) { return "0.0 bytes"; }

      int mag = (int)Math.Log(value, 1024);
      decimal adjustedSize = (decimal)value / (1L << (mag * 10));

      return string.Format("{0:n1}{1}", adjustedSize, SizeSuffixes[mag]);
    }
    #endregion

    #region Config File methods

    /// <summary>
    /// Set the App to run at Windows startup
    /// </summary>
    /// <param name="_enabled"></param>
    private void SetStartUp(bool _enabled)
    {
      // Using Microsoft.Win32.TaskScheduler
      //using (TaskService ts = new TaskService())
      //{
      //  string action = System.Reflection.Assembly.GetExecutingAssembly().Location;
      //  string taskName = "CoolestControlCenter";

      //  if (_enabled)
      //  {
      //    if (ts.GetTask(taskName) == null)
      //    {
      //      TaskDefinition td = ts.NewTask();
      //      td.Principal.RunLevel = TaskRunLevel.Highest;
      //      td.RegistrationInfo.Author = identity.Name;
      //      td.RegistrationInfo.Description = "This task starts Coolest Control Center on Windows startup.";
      //      //td.Triggers.Add(new TimeTrigger(DateTime.Now));
      //      td.Actions.Add(new ExecAction(action, null));
      //      ts.RootFolder.RegisterTaskDefinition(taskName, td);
      //    }
      //  }
      //  else
      //  {
      //    if (ts.FindTask(taskName) != null)
      //      ts.RootFolder.DeleteTask(taskName, false);
      //  }

      //  UpdateAppSettings("appSettings", "StartUp", _enabled.ToString());
      //  ts.Dispose();
      //}

      //menuStartUp.IsChecked = _enabled;

      // Using registry key
      RegistryKey CheckRegistry = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

      if (_enabled)
      {
        if (CheckRegistry.GetValue("CoolestControlCenter") == null)
          CheckRegistry.SetValue("CoolestControlCenter", System.Reflection.Assembly.GetExecutingAssembly().Location);
      }
      else
      {
        if (CheckRegistry.GetValue("CoolestControlCenter") != null)
          CheckRegistry.DeleteValue("CoolestControlCenter", false);
      }

      menuStartUp.IsChecked = _enabled;
      UpdateAppSettings("appSettings", "StartUp", _enabled.ToString());
      CheckRegistry.Close();
      CheckRegistry.Dispose();
    }

    public void LoadConfigFile()
    {
      try
      {
        if (!File.Exists(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile)) CreateConfigFile();

        // Clean the menu. (fastest/better way to do it?)
        foreach (System.Windows.Controls.MenuItem item in menuChangeOpacity.Items)
          item.IsChecked = false;

        if (GetAppSettingValue("opacity") == "1") opacity100.IsChecked = true;
        if (GetAppSettingValue("opacity") == "0.8") opacity80.IsChecked = true;
        if (GetAppSettingValue("opacity") == "0.6") opacity60.IsChecked = true;
        if (GetAppSettingValue("opacity") == "0.4") opacity40.IsChecked = true;
        Opacity = Convert.ToDouble(GetAppSettingValue("opacity"));

        bool IsMonitoring = Convert.ToBoolean(GetAppSettingValue("Monitoring"));
        menuMonitoring.IsChecked = IsMonitoring;
        Monitoring(IsMonitoring);

        // Clean the menu. (fastest/better way to do it?)
        foreach (System.Windows.Controls.MenuItem item in menuChangeFanProfile.Items)
          item.IsChecked = false;

        if (GetAppSettingValue("FanProfile") == "0") menuFanSilent.IsChecked = true;
        if (GetAppSettingValue("FanProfile") == "1") menuFanBalanced.IsChecked = true;
        if (GetAppSettingValue("FanProfile") == "2") menuFanAutomatic.IsChecked = true;
        if (GetAppSettingValue("FanProfile") == "3") menuFanTurbo.IsChecked = true;

        SetMePosition(Convert.ToInt32(GetAppSettingValue("left")), Convert.ToInt32(GetAppSettingValue("top")));

        SetStartUp(Convert.ToBoolean(GetAppSettingValue("StartUp")));
      }
      catch (Exception ex)
      {
        Console.WriteLine("Error loading config file => " + ex.Message);
      }
    }

    public string GetAppSettingValue(string key)
    {
      try
      {
        NameValueCollection appSettings = ConfigurationManager.AppSettings;

        string[] arr = appSettings.GetValues(key);
        return arr[0];
      }
      catch (ConfigurationErrorsException e)
      {
        Console.WriteLine("Get app settings => [CreateAppSettings: {0}]", e.ToString());
        return e.ToString();
      }
      catch (Exception e)
      {
        Console.WriteLine("Get app setting => [CreateAppSettings: {0}]", e.ToString());
        return e.ToString();
      }
    }

    public void UpdateAppSettings(string sectionName, string key, string value)
    {
      try
      {
        Configuration configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        if (ConfigurationManager.AppSettings.AllKeys.Contains(key))
          configFile.AppSettings.Settings[key].Value = value;
        else
          configFile.AppSettings.Settings.Add(key, value);

        configFile.Save(ConfigurationSaveMode.Modified);
        ConfigurationManager.RefreshSection(sectionName);
      }
      catch (ConfigurationErrorsException e)
      {
        Console.WriteLine("Error updating app settings => [CreateAppSettings: {0}]", e.ToString());
      }
    }

    public void CreateConfigFile()
    {
      string[] content = {
        "<?xml version=\"1.0\" encoding=\"utf-8\" ?>",
        "<configuration>",
        "<startup>",
        "<supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\" />",
        "</startup>",
        "<appSettings>",
        "<add key=\"Culture\" value=\"en-EN\" />",
        "<add key=\"Monitoring\" value=\"true\" />",
        "<add key=\"StartUp\" value=\"false\" />",
        "<add key=\"FanProfile\" value=\"0\" />",
        "<add key=\"left\" value=\"990\" />",
        "<add key=\"top\" value=\"305\" />",
        "<add key=\"opacity\" value=\"1\" />",
        "</appSettings>",
        "</configuration>",
      };

      string pathString = Convert.ToString(AppDomain.CurrentDomain.BaseDirectory) + AppName + ".Config";
      //string pathString = CurrentDirectory + @"\" + AppName + ".Config";
      File.WriteAllLines(pathString, content);
      ConfigurationManager.RefreshSection("appSettings");
    }
    #endregion

    #region Power management methods

    [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveScheme")]
    public static extern uint PowerSetActiveScheme(IntPtr UserPowerKey, ref Guid ActivePolicyGuid);

    /// <summary>
    /// Get notified if power status changes AC/DC
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
      if (e.Mode == PowerModes.StatusChange)
      {
        // Running on battery
        if (System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline)
        {
          // Automatic change to Balanced/Power saver
          // TODO ask the user for the value
          Guid guid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
          PowerSetActiveScheme(IntPtr.Zero, ref guid);

          GetPowerPlan();
        }
      }
    }

    /// <summary>
    /// Get all supported power plans and fill the menu with their names
    /// </summary>
    private void GetPowerPlan()
    {
      menuChangePowerProfile.Items.Clear();

      foreach (ManagementObject obj in PowerPlanObject.Get())
      {
        System.Windows.Controls.MenuItem item = new System.Windows.Controls.MenuItem();
        item.IsCheckable = true;
        item.Header = obj["ElementName"].ToString().Trim();
        item.Click += new RoutedEventHandler(MenuPowerPlanHandler);

        if ((bool)obj["IsActive"])
          item.IsChecked = true;

        menuChangePowerProfile.Items.Add(item);
      }

      PowerPlanObject.Dispose();
    }

    /// <summary>
    /// The menu responses for power plans are controlled here.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MenuPowerPlanHandler(object sender, RoutedEventArgs e)
    {
      //The parameter(item) is received through "sender" to be able to make the query through WMI
      System.Windows.Controls.MenuItem item = sender as System.Windows.Controls.MenuItem;
      ManagementObjectSearcher PowerPlan = new ManagementObjectSearcher(@"root\cimv2\power", string.Format("select * FROM Win32_PowerPlan where ElementName LIKE '{0}'", item.Header.ToString().Trim()));

      //Using a foreach we look for the plan that was selected in the menu
      foreach (ManagementObject obj in PowerPlan.Get())
      {
        // Here we must clean the value returned in the object (obj) of the query to be able to get
        // ONLY the value that PowerSetActiveScheme format supports: remove root string and '{' '}' chars
        // working on a better way to do this!!!
        string output = obj["InstanceID"].ToString().Substring(obj["InstanceID"].ToString().IndexOf("{") + 1).Trim();
        if (output.Contains('}'))
          output = output.Substring(0, output.LastIndexOf('}'));

        // The string must be converted again to Guid to activate the plan
        Guid guid = new Guid(output);
        PowerSetActiveScheme(IntPtr.Zero, ref guid);
      }

      GetPowerPlan();
    }

    /// <summary>
    /// 
    /// </summary>
    private void GetBatteryStatus()
    {
      double BatteryStatus = Math.Round(System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent * 100, 1);
      double BatteryRemaining = Math.Round((double)System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifeRemaining / 3600, 1);
      string sTime = "h";

      if (System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline)
      {
        if (BatteryRemaining < 1)
        {
          sTime = "min";
          BatteryRemaining = Math.Round((double)System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifeRemaining / 60, 1);
        }
        Dispatcher.Invoke(() => { ProgressBattery.ToolTip = string.Format("Time left {0} {1} ({2}%)", BatteryRemaining, sTime, BatteryStatus); });
        //Dispatcher.Invoke(() => { ProgressBattery.Foreground = new LinearGradientBrush(Colors.DarkGreen, Colors.LawnGreen, 90); });
      }
      else
      {
        Dispatcher.Invoke(() => { ProgressBattery.ToolTip = string.Format("Charging {0}%", BatteryStatus); });
        //Dispatcher.Invoke(() => { ProgressBattery.Foreground = new LinearGradientBrush(Colors.Blue, Colors.DarkBlue, 90); });
      }

      Dispatcher.Invoke(() => { ProgressBattery.Value = BatteryStatus; });
      //Dispatcher.Invoke(() => { ProgressBattery.ToolTip = string.Format("Time left {0} {1} ({2}%)", BatteryRemaining, sTime, BatteryStatus); });
      Thread.Sleep(1000);
    }
    #endregion


    /// <summary>
    /// Set the position of the main window relative to the desktop
    /// </summary>
    /// <param name="_left"></param>
    /// <param name="_top"></param>
    private void SetMePosition(int _left, int _top)
    {
      WindowStartupLocation = WindowStartupLocation.Manual;
      Left = _left;
      Top = _top;
    }

    /// <summary>
    /// Start/Stop monitoring
    /// </summary>
    public void Monitoring(bool state)
    {
      if (state)
      {
        if (Visibility == Visibility.Hidden) Visibility = Visibility.Visible;
        UpdateAppSettings("appSettings", "Monitoring", "true");
        dispatcherTimer.Start();
      }
      else
      {
        dispatcherTimer.Stop();

        ProgressCPU.Value = 0;
        label4.Content = "Total usage: 0%";
        ProgressGPU.Value = 0;
        label9.Content = "Total usage: 0%";
        ProgressHDD.Value = 0;
        ProgressRAM.Value = 0;
        ProgressBattery.Value = 0;
        ProgressBattery.ToolTip = null;
        lblCpuFrequency.Content = "0Mhz";
        lblCpuFan.Content = "0rpm";
        lblCpuTemp.Content = "0°C";
        lblGpuFrequency.Content = "0Mhz";
        lblGpuMemory.Content = "0Mhz";
        lblGpuTemp.Content = "0°C";
        lblGpuFan.Content = "0rpm";

        GC.Collect();
      }
    }

    /// <summary>
    /// Get the manufacturer/vendor name
    /// </summary>
    /// <returns>string result</returns>
    public string GetMachineVendor()
    {
      string result = "";

      foreach (ManagementObject obj in ComputerSystemObject.Get())
      {
        result = obj["Model"].ToString();
      }

      ComputerSystemObject.Dispose();
      return result;
    }


    /// <summary>
    /// Get the total phisical memory
    /// </summary>
    private void GetTotalMemory()
    {
      ulong TotalPhysicalMemory = 0;
      ulong FreePhysicalMemory = 0;
      ulong UsedPhysicalMemory = 0;

      foreach (ManagementObject obj in RAMObject.Get())
      {
        TotalPhysicalMemory = (ulong)obj["TotalVisibleMemorySize"];
        FreePhysicalMemory = (ulong)obj["FreePhysicalMemory"];
        UsedPhysicalMemory = TotalPhysicalMemory - FreePhysicalMemory;
      }
      // We must multiply the value get from WMI for 1024 to get the total RAM in bytes
      // then convert using SizeSuffix method
      Dispatcher.Invoke(() => { lblTotalRAM.Content = "RAM " + SizeSuffix((long)Convert.ToDouble(UsedPhysicalMemory * 1024)) + "/" + SizeSuffix((long)Convert.ToDouble(TotalPhysicalMemory * 1024)); });
      Dispatcher.Invoke(() => { ProgressRAM.Value = Convert.ToInt64(100 - FreePhysicalMemory / (double)TotalPhysicalMemory * 100); });

      RAMObject.Dispose();
      Thread.Sleep(1000);
    }


    /// <summary>
    /// Get the Fan speed in RPM from CPU/GPU using ASUS WMI Query
    /// FAN ID: Decimal => 1114131(CPU)/1114132(GPU) | Hexadecimal => 0x00110013/0x00110014
    /// </summary>
    /// <WMI inParams name="Device_ID"></param>
    /// <WMI outParams name="device_status"></param>
    /// <param name="DeviceId"></param>
    public int GetFanSpeed(int DeviceId)
    {
      int rpm = 0;
      try
      {
        foreach (ManagementObject asusClass in AsusAtkObject.Get())
        {
          ManagementBaseObject inParams = asusClass.GetMethodParameters("DSTS");
          inParams["Device_ID"] = DeviceId;
          ManagementBaseObject outParams = asusClass.InvokeMethod("DSTS", inParams, null);

          // Convert to string and Hexadecimal to get RPMs
          string outputHex = Convert.ToString(int.Parse(outParams["device_status"].ToString()), 16);
          rpm = (Convert.ToInt32(outputHex, 16) - 0x00010000) * 0x64;
          asusClass.Dispose();
        }
        AsusAtkObject.Dispose();
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.Message);
      }

      return rpm;
    }

    /// <summary>
    /// Provides precalculated data from performance counters that monitor the CPU
    /// It can be used Win32_PerfFormattedData_PerfOS_Processor instead to get all cores
    /// </summary>
    public void GetCpuInformation()
    {
      // We must call twice CpuCounter.NextValue() and use Thread.Sleep to get the proper/real value
      CpuFrequency.NextValue();
      CpuCounter.NextValue();
      Thread.Sleep(500);
      double CpuValue = CpuFrequency.NextValue();
      double CurrentTemperature = CpuTemperature.NextValue() - 2732 / 10; // Set °C
      double CurrentUsage = CpuCounter.NextValue();

      // To get REAL CPU's speed we must use WMI and PerformanceCounter. A better way???
      foreach (ManagementObject obj in CPUObject.Get())
      {
        double maxSpeed = Convert.ToDouble(obj["MaxClockSpeed"]) / 1000;
        double CurrentSpeed = maxSpeed * CpuValue / 100;

        Dispatcher.Invoke(() => { lblCpuFrequency.Content = string.Format("{0:0.00}Ghz", CurrentSpeed); });
        Dispatcher.Invoke(() => { lblCpuName.Content = obj["Name"]; });
        Dispatcher.Invoke(() => { lblCpuName.ToolTip = obj["Name"]; });
      }

      Dispatcher.Invoke(() => { lblCpuTemp.Content = string.Format("{0}°C", CurrentTemperature); });
      Dispatcher.Invoke(() => { lblCpuFan.Content = GetFanSpeed(1114131) + "rpm"; });
      Dispatcher.Invoke(() => { ProgressCPU.Value = (int)CurrentUsage; });
      Dispatcher.Invoke(() => { label4.Content = string.Format("Total usage: {0:0}%", CurrentUsage); });

      /* Get temps using WMI instead of PerformanceCounter
       * Test result: PerformanceCounter seems to be more fast and accurate
      foreach (ManagementObject obj in CPUTempObject.Get())
      {
        double _value = Convert.ToDouble(Convert.ToDouble(obj.GetPropertyValue("CurrentTemperature").ToString()) - 2732) / 10;
        Dispatcher.Invoke(() => { lblCpuTemp.Content = _value + "°C"; });
      }
      CPUTempObject.Dispose();
      */

      CPUObject.Dispose();
      CpuFrequency.Dispose();
      CpuCounter.Dispose();
      CpuTemperature.Dispose();
    }

    /// <summary>
    /// Get the computer's full name
    /// </summary>
    /// <returns></returns>
    //public string GetCpuName()
    //{
    //  string result = "";
    //  foreach (ManagementObject obj in CPUObject.Get())
    //  {
    //    result += obj["Name"];
    //  }

    //  CPUObject.Dispose();
    //  return result;
    //}

    /// <summary>
    /// Get the GPU's full name
    /// </summary>
    /// <returns></returns>
    //public string GetGpuName()
    //{
    //  string result = "";
    //  foreach (ManagementObject obj in GPUObject.Get())
    //  {
    //    result += obj["Name"] + " " + SizeSuffix((long)Convert.ToDouble(obj["AdapterRAM"])) + Environment.NewLine;
    //  }

    //  GPUObject.Dispose();
    //  return result;
    //}

    /// <summary>
    /// Get GPU information using NvAPIWrapper
    /// </summary>
    public void GetGpuInformation()
    {
      Dispatcher.Invoke(() => { lblGpuFan.Content = GetFanSpeed(1114132) + "rpm"; });

      try
      {
        foreach (PhysicalGPU gpu in PhysicalGPU.GetPhysicalGPUs())
        {
          foreach (GPUThermalSensor gpuSensor in gpu.ThermalInformation.ThermalSensors)
          {
            Dispatcher.Invoke(() => { lblGpuTemp.Content = gpuSensor.CurrentTemperature + "°C"; });
          }

          double _total = Math.Round((double)gpu.MemoryInformation.DedicatedVideoMemoryInkB / 1000 / 1000, 2);
          double _available = Math.Round((double)gpu.MemoryInformation.AvailableDedicatedVideoMemoryInkB / 1000 / 1000, 2);

          Dispatcher.Invoke(() => { lblGpuName.Content = gpu.FullName; });
          Dispatcher.Invoke(() => { lblGpuName.ToolTip = "Total memory: " + _total + "GB" + Environment.NewLine + "Available memory: " + _available + "GB"; });
          Dispatcher.Invoke(() => { ProgressGPU.Value = gpu.UsageInformation.GPU.Percentage; });
          Dispatcher.Invoke(() => { label9.Content = string.Format("Total usage: {0:0}%", gpu.UsageInformation.GPU.Percentage); });
          Dispatcher.Invoke(() => { lblGpuMemory.Content = gpu.CurrentClockFrequencies.MemoryClock.Frequency / 1000 + "Mhz"; });
          Dispatcher.Invoke(() => { lblGpuFrequency.Content = gpu.CurrentClockFrequencies.GraphicsClock.Frequency / 1000 + "Mhz"; });
        }
      }
      catch (NvAPIWrapper.Native.Exceptions.NVIDIAApiException ex)
      {
        if (ex.Message == "NVAPI_GPU_NOT_POWERED")
        {
          Dispatcher.Invoke(() => { ProgressGPU.Value = 0; });
          Dispatcher.Invoke(() => { lblGpuTemp.Content = "0°C"; });
          Dispatcher.Invoke(() => { lblGpuMemory.Content = "Energy saving"; });
          Dispatcher.Invoke(() => { lblGpuFrequency.Content = "Energy saving"; });
        }
        if (ex.Message == "NVAPI_NOT_SUPPORTED")
        {
          Dispatcher.Invoke(() => { ProgressGPU.Value = 0; });
          Dispatcher.Invoke(() => { lblGpuTemp.Content = "Not supported"; });
          Dispatcher.Invoke(() => { lblGpuMemory.Content = "Not supported"; });
          Dispatcher.Invoke(() => { lblGpuFrequency.Content = "Not supported"; });
        }
        return;
      }
      Thread.Sleep(500);
    }

    /// <summary>
    /// Get main drive (C) information
    /// </summary>
    /// <param name="driveLetter"></param>
    public void GetDriveInfo(string driveLetter)
    {
      // For now only accept C
      DriveInfo drive = new DriveInfo(driveLetter);
      long UsedHDDSpace = drive.TotalSize - drive.AvailableFreeSpace;

      Dispatcher.Invoke(() => { lblHDD.Content = drive.Name + " " + SizeSuffix((long)Convert.ToDouble(UsedHDDSpace)) + "/" + SizeSuffix((long)Convert.ToDouble(drive.TotalSize)); });
      Dispatcher.Invoke(() => { ProgressHDD.ToolTip = drive.Name + drive.VolumeLabel + Environment.NewLine + "Format: " + drive.DriveFormat + Environment.NewLine + "Type: " + drive.DriveType + Environment.NewLine; });
      Dispatcher.Invoke(() => { ProgressHDD.Value = Convert.ToInt64(100 - drive.AvailableFreeSpace / (double)drive.TotalSize * 100); });

      Thread.Sleep(1000);
    }

    /// <summary>
    /// Main dispatcher timer
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void dispatcherTimer_Tick(object sender, EventArgs e)
    {
      ThreadCpuUsage = new Thread(() => GetCpuInformation());
      ThreadCpuUsage.Start();

      ThreadGpuInformation = new Thread(() => GetGpuInformation());
      ThreadGpuInformation.Start();

      ThreadMemory = new Thread(() => GetTotalMemory());
      ThreadMemory.Start();

      ThreadDrives = new Thread(() => GetDriveInfo("C"));
      ThreadDrives.Start();

      ThreadBattery = new Thread(() => GetBatteryStatus());
      ThreadBattery.Start();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void FrmMain_Loaded(object sender, RoutedEventArgs e)
    {
      ShowInTaskbar = false;
      notifyIcon.Visible = true;
      lblMachineName.Content = GetMachineVendor();
      lblMachineName.ToolTip = GetMachineVendor();
      lblCpuName.Content = "";
      lblGpuName.Content = "";
      GetPowerPlan();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void FrmMain_Activated(object sender, EventArgs e)
    {
      menuClose.IsEnabled = true;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void FrmMain_MouseDown(object sender, MouseButtonEventArgs e)
    {
      if (e.ChangedButton == MouseButton.Left)
      {
        DragMove();
        UpdateAppSettings("appSettings", "left", Math.Round(Left).ToString());
        UpdateAppSettings("appSettings", "top", Math.Round(Top).ToString());
      }
    }

    #region Context Menu clicks events

    /// <summary>
    /// Context Menu clicks events
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MenuItemsClick(object sender, RoutedEventArgs e)
    {
      if (sender == menuClose)
      {
        Hide();
        Monitoring(false);
        menuClose.IsEnabled = false;
        menuMonitoring.IsChecked = false;
        UpdateAppSettings("appSettings", "Monitoring", "false");
      }
      if (sender == menuExit)
      {
        Hide();
        Monitoring(false);

        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        notifyIcon = null;

        System.Windows.Application.Current.Shutdown();
        //Environment.Exit(0);
      }

      if (sender == menuMonitoring)
      {
        if (menuMonitoring.IsChecked == true)
        {
          Monitoring(true);
          UpdateAppSettings("appSettings", "Monitoring", "true");
        }
        else
        {
          Monitoring(false);
          UpdateAppSettings("appSettings", "Monitoring", "false");
        }
        return;
      }

      if (sender == menuStartUp)
      {
        if (menuStartUp.IsChecked == true)
          SetStartUp(true);
        else
          SetStartUp(false);
        return;
      }

      if (sender == menuFanSilent)
        UpdateAppSettings("appSettings", "FanProfile", "0");
      if (sender == menuFanBalanced)
        UpdateAppSettings("appSettings", "FanProfile", "1");
      if (sender == menuFanAutomatic)
        UpdateAppSettings("appSettings", "FanProfile", "2");
      if (sender == menuFanTurbo)
        UpdateAppSettings("appSettings", "FanProfile", "3");

      if (sender == opacity100)
        UpdateAppSettings("appSettings", "opacity", "1");
      if (sender == opacity80)
        UpdateAppSettings("appSettings", "opacity", "0.8");
      if (sender == opacity60)
        UpdateAppSettings("appSettings", "opacity", "0.6");
      if (sender == opacity40)
        UpdateAppSettings("appSettings", "opacity", "0.4");

      LoadConfigFile();
    }

    #endregion

    private void ImgMenu_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => contextMenu.IsOpen = true;

    private void FrmMain_Initialized(object sender, EventArgs e)
    {
      notifyIcon = new NotifyIcon();
      notifyIcon.MouseDown += new System.Windows.Forms.MouseEventHandler(notifyIcon_MouseDown);
      notifyIcon.Icon = Properties.Resources.icon;
      notifyIcon.Text = "Left click to show de UI / Right click to show the menu";
    }

    void notifyIcon_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
    {
      if (e.Button == MouseButtons.Left) { Show(); Activate(); }
      if (e.Button == MouseButtons.Right) contextMenu.IsOpen = true;
    }

    private void FrmMain_Closed(object sender, EventArgs e)
    {
      //
    }

    private void FrmMain_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
      // Just to make sure
      if (ThreadCpuUsage.IsAlive)
        ThreadCpuUsage.Abort();
      if (ThreadGpuInformation.IsAlive)
        ThreadCpuUsage.Abort();

      GC.Collect();
    }
  }
}