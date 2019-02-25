#region Copyright (C) 2007-2018 Team MediaPortal

/*
    Copyright (C) 2007-2018 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using MediaPortal.Common;
using MediaPortal.Common.Logging;
using MediaPortal.Common.Messaging;
using MediaPortal.Common.PluginManager;
using MediaPortal.Common.Settings;
using MediaPortal.Plugins.InputDeviceManager.Models;
using MediaPortal.Plugins.InputDeviceManager.RawInput;
using MediaPortal.UI.Control.InputManager;
using MediaPortal.UI.General;
using MediaPortal.UI.Presentation.Screens;
using MediaPortal.UI.Presentation.Workflow;
using SharpLib.Hid;
using SharpLib.Win32;

namespace MediaPortal.Plugins.InputDeviceManager
{
  public class InputDeviceManager : IPluginStateTracker
  {
    private const bool CAPTURE_ONLY_IN_FOREGROUND = true;
    private const bool SUPPORT_REPEATS = true;
    private const int WM_KEYDOWN = 0x100;
    private const int WM_KEYUP = 0x101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private static readonly ConcurrentDictionary<string, InputDevice> _inputDevices = new ConcurrentDictionary<string, InputDevice>();
    private static IScreenControl _screenControl;
    private static readonly ConcurrentDictionary<string, long> _pressedKeys = new ConcurrentDictionary<string, long>();
    private static SharpLib.Hid.Handler _hidHandler;
    private static List<Action<object, SharpLib.Hid.Event, string, IDictionary<string, long>>> _externalKeyPressHandlers = new List<Action<object, SharpLib.Hid.Event, string, IDictionary<string, long>>>();
    private static object _listSyncObject = new object();
    private static Message _currentMessage;
    private static bool _currentMessageHandled;
    private static ConcurrentDictionary<long, (string Name, long Code)> _genericKeyDownEvents = new ConcurrentDictionary<long, (string Name, long Code)>();
    private static readonly ConcurrentDictionary<string, long> _pressedPreviewKeys = new ConcurrentDictionary<string, long>();
    private static System.Timers.Timer _pressedKeyTimer = new System.Timers.Timer(2000);
    private static Dictionary<UsagePage, Dictionary<long, Key>> _defaultRemoteKeyCodes = new Dictionary<UsagePage, Dictionary<long, Key>>();
    private static Dictionary<UsagePage, Dictionary<long, string>> _defaultRemoteScreenCodes = new Dictionary<UsagePage, Dictionary<long, string>>();
    private static readonly Key[] _navigationKeys = new[] { Key.Ok, Key.Escape, Key.Left, Key.Right, Key.Up, Key.Down };

    private SynchronousMessageQueue _messageQueue;

    public static InputDeviceManager Instance { get; private set; }
    public static IDictionary<string, InputDevice> InputDevices
    {
      get { return _inputDevices; }
    }

    #region Initialization

    public InputDeviceManager()
    {
      Instance = this;

      _pressedKeyTimer.AutoReset = false;
      _pressedKeyTimer.Elapsed += (s, e) =>
      {
        if (_pressedPreviewKeys.Count > 0 || _pressedKeys.Count > 0)
          ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Pressed keys reset");

        _pressedPreviewKeys.Clear();
        _genericKeyDownEvents.Clear();
        _pressedKeys.Clear();
      };
    }

    private static void StartThread()
    {
      while (_screenControl == null)
      {
        try
        {
          if (ServiceRegistration.IsRegistered<IScreenControl>())
          {
            _screenControl = ServiceRegistration.Get<IScreenControl>();

            SharpLib.Win32.RawInputDeviceFlags flags = CAPTURE_ONLY_IN_FOREGROUND ? 0 : SharpLib.Win32.RawInputDeviceFlags.RIDEV_INPUTSINK;
            //SharpLib.Win32.RawInputDeviceFlags flags = SharpLib.Win32.RawInputDeviceFlags.RIDEV_EXINPUTSINK;
            //SharpLib.Win32.RawInputDeviceFlags flags = SharpLib.Win32.RawInputDeviceFlags.RIDEV_INPUTSINK;
            IntPtr handle = _screenControl.MainWindowHandle;
            List<RAWINPUTDEVICE> devices = new List<RAWINPUTDEVICE>();
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.WindowsMediaCenterRemoteControl,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.WindowsMediaCenter.WindowsMediaCenterRemoteControl,
              dwFlags = flags,
              hwndTarget = handle
            });
            //devices.Add(new RAWINPUTDEVICE
            //{
            //  usUsagePage = (ushort)SharpLib.Hid.UsagePage.WindowsMediaCenterRemoteControl,
            //  usUsage = (ushort)SharpLib.Hid.UsageCollection.WindowsMediaCenter.WindowsMediaCenterLowLevel,
            //  dwFlags = flags,
            //  hwndTarget = handle
            //});

            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.Consumer,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.Consumer.ConsumerControl,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.Consumer,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.Consumer.ApplicationLaunchButtons,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.Consumer,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.Consumer.FunctionButtons,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.Consumer,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.Consumer.GenericGuiApplicationControls,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.Consumer,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.Consumer.MediaSelection,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.Consumer,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.Consumer.NumericKeyPad,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.Consumer,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.Consumer.PlaybackSpeed,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.Consumer,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.Consumer.ProgrammableButtons,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.Consumer,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.Consumer.SelectDisc,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.Consumer,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.Consumer.Selection,
              dwFlags = flags,
              hwndTarget = handle
            });

            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.GenericDesktopControls,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.GenericDesktop.SystemControl,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.GenericDesktopControls,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.GenericDesktop.GamePad,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.GenericDesktopControls,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.GenericDesktop.Joystick,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.GenericDesktopControls,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.GenericDesktop.Keyboard,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.GenericDesktopControls,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.GenericDesktop.KeyPad,
              dwFlags = flags,
              hwndTarget = handle
            });
            devices.Add(new RAWINPUTDEVICE
            {
              usUsagePage = (ushort)SharpLib.Hid.UsagePage.GenericDesktopControls,
              usUsage = (ushort)SharpLib.Hid.UsageCollection.GenericDesktop.Mouse,
              dwFlags = flags,
              hwndTarget = handle
            });

            _hidHandler = new SharpLib.Hid.Handler(devices.ToArray(), true, -1, -1);
            _hidHandler.OnHidEvent += new Handler.HidEventHandler(OnHidEvent);
          }
        }
        catch (Exception ex)
        {
          // ignored
          ServiceRegistration.Get<ILogger>().Error("InputDeviceManager: Failure to register HID handler", ex);
        }
        Thread.Sleep(500);
      }
    }

    #endregion

    #region Form key handling

    private void OnPreviewMessage(SynchronousMessageQueue queue)
    {
      try
      {
        SystemMessage message;
        while ((message = queue.Dequeue()) != null)
        {
          if (message.ChannelName == WindowsMessaging.CHANNEL)
          {
            WindowsMessaging.MessageType messageType = (WindowsMessaging.MessageType)message.MessageType;
            switch (messageType)
            {
              case WindowsMessaging.MessageType.WindowsBroadcast:
                _currentMessage = (Message)message.MessageData[WindowsMessaging.MESSAGE];
                _currentMessageHandled = false;
                //WM_KEYDOWN and WM_KEYUP are not handled by SharpLibHid so we need to handle them to avoid a duplicate key press
                if (_externalKeyPressHandlers.Count == 0 && (_currentMessage.Msg == WM_KEYDOWN || _currentMessage.Msg == WM_KEYUP || _currentMessage.Msg == WM_SYSKEYDOWN || _currentMessage.Msg == WM_SYSKEYUP))
                {
                  var key = ConvertSystemKey((Keys)_currentMessage.WParam);
                  if (_currentMessage.Msg == WM_KEYDOWN || _currentMessage.Msg == WM_SYSKEYDOWN)
                  {
                    _pressedPreviewKeys.TryAdd(key.ToString(), (long)key);
                    RestartKeyPressedTimer();
                  }
                  if (_inputDevices.TryGetValue("Keyboard", out InputDevice device))
                  {
                    if (device.KeyMap.Any(m => KeyCombinationsMatch(m.Codes.Select(c => c.Code), _pressedPreviewKeys.Values)))
                    {
                      _currentMessageHandled = true;
                      ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Preview message handled for keys: {0}", string.Join(", ", _pressedPreviewKeys.Keys));
                    }
                  }
                  if (_currentMessage.Msg == WM_KEYUP || _currentMessage.Msg == WM_SYSKEYUP)
                  {
                    _pressedPreviewKeys.TryRemove(key.ToString(), out _);
                  }
                }
                _hidHandler?.ProcessInput(ref _currentMessage);
                message.MessageData[WindowsMessaging.MESSAGE] = _currentMessage;
                if (_currentMessageHandled)
                  message.MessageData[WindowsMessaging.HANDLED] = true;
                break;
            }
          }
        }
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Error("InputDeviceManager: Preview message failed", ex);
      }
    }

    private void SubscribeToMessages()
    {
      if (_messageQueue != null)
        return;
      _messageQueue = new SynchronousMessageQueue(this, new[] { WindowsMessaging.CHANNEL });
      _messageQueue.MessagesAvailable += OnPreviewMessage;
      _messageQueue.RegisterAtAllMessageChannels();
    }

    private void UnsubscribeFromMessages()
    {
      if (_messageQueue == null)
        return;
      _messageQueue.Dispose();
      _messageQueue = null;
    }

    #endregion

    #region Logging   

    private static string GetLogEventData(string info, SharpLib.Hid.Event hidEvent)
    {
      string str = string.IsNullOrEmpty(info) ? "" : $"{info} "; 
      str += "HID Event";
      if (hidEvent.IsButtonDown)
        str += ", DOWN";
      if (hidEvent.IsButtonUp)
        str += ", UP";
      if (hidEvent.IsGeneric)
      {
        str += ", Generic";
        for (int aIndex = 0; aIndex < hidEvent.Usages.Count; ++aIndex)
          str += ", Usage: " + hidEvent.UsageNameAndValue(aIndex);
        str += ", UsagePage: " + hidEvent.UsagePageNameAndValue() + ", UsageCollection: " + hidEvent.UsageCollectionNameAndValue() + ", Input Report: 0x" + hidEvent.InputReportString();
        if (hidEvent.Device?.IsGamePad ?? false)
        {
          str += ", GamePad, DirectionState: " + hidEvent.GetDirectionPadState();
        }
        else if (hidEvent.UsagePageEnum == UsagePage.WindowsMediaCenterRemoteControl)
        {
          str += ", Remote";
        }
        else if (hidEvent.UsagePageEnum == UsagePage.Consumer)
        {
          str += ", Consumer";
        }
        else if (hidEvent.UsagePageEnum == UsagePage.SimulationControls)
        {
          str += ", Sim";
        }
        else if (hidEvent.UsagePageEnum == UsagePage.Telephony)
        {
          str += ", Mobile";
        }
      }
      else if (hidEvent.IsKeyboard)
        str += ", Keyboard" + (object)", Virtual Key: " + hidEvent.VirtualKey.ToString();
      else if (hidEvent.IsMouse)
        str += ", Mouse, Flags: " + hidEvent.RawInput.mouse.buttonsStr.usButtonFlags;
      if (hidEvent.IsBackground)
        str += ", Background";
      if (hidEvent.IsRepeat)
        str += ", Repeat: " + hidEvent.RepeatCount;
      if (hidEvent.HasModifierAlt)
        str += ", AltKey";
      if (hidEvent.HasModifierControl)
        str += ", ControlKey";
      if (hidEvent.HasModifierShift)
        str += ", ShiftKey";
      if (hidEvent.HasModifierWindows)
        str += ", WindowsKey";
      if (!string.IsNullOrEmpty(hidEvent.Device?.FriendlyName))
        str += ", FriendlyName: " + hidEvent.Device.FriendlyName;
      if (!string.IsNullOrEmpty(hidEvent.Device?.Manufacturer))
        str += ", Manufacturer: " + hidEvent.Device.Manufacturer;
      if (!string.IsNullOrEmpty(hidEvent.Device?.Product))
        str += ", Product: " + hidEvent.Device.Product;
      if (!string.IsNullOrEmpty(hidEvent.Device?.ProductId.ToString()))
        str += ", ProductId: " + hidEvent.Device.ProductId.ToString();
      if (!string.IsNullOrEmpty(hidEvent.Device?.VendorId.ToString()))
        str += ", VendorId: " + hidEvent.Device.VendorId.ToString();
      if (!string.IsNullOrEmpty(hidEvent.Device?.Version.ToString()))
        str += ", Version: " + hidEvent.Device.Version.ToString();

      return str;
    }

    private static void LogEvent(string info, SharpLib.Hid.Event hidEvent)
    {
      ServiceRegistration.Get<ILogger>().Debug(GetLogEventData(info, hidEvent));
    }

    #endregion

    #region HID event handling

    private static bool TryDecodeEvent(SharpLib.Hid.Event hidEvent, out string device, out string name, out long code, out bool buttonUp, out bool buttonDown)
    {
      device = "";
      name = "";
      code = 0;
      buttonUp = hidEvent.IsButtonUp;
      buttonDown = hidEvent.IsButtonDown;

      if ((hidEvent.IsMouse && hidEvent.RawInput.mouse.buttonsStr.usButtonFlags > 0) || !hidEvent.IsMouse)
        ServiceRegistration.Get<ILogger>().Debug(GetLogEventData("Log", hidEvent));

      if (hidEvent.IsKeyboard)
      {
        if (hidEvent.VirtualKey != Keys.None)
        {
          if (hidEvent.VirtualKey != Keys.Escape)
          {
            var key = ConvertSystemKey(hidEvent.VirtualKey);
            device = "Keyboard";
            name = KeyMapper.GetMicrosoftKeyName((int)key);
            code = (long)key;
          }
          else
          {
            return false; //Escape reserved for dialog close
          }
        }
        else
        {
          LogEvent("Invalid key", hidEvent);
          return false; //Unsupported
        }
      }
      else if (hidEvent.IsMouse)
      {
        device = "Mouse";
        int id = 0;
        switch (hidEvent.RawInput.mouse.buttonsStr.usButtonFlags)
        {
          case RawInputMouseButtonFlags.RI_MOUSE_LEFT_BUTTON_DOWN:
          case RawInputMouseButtonFlags.RI_MOUSE_LEFT_BUTTON_UP:
          case RawInputMouseButtonFlags.RI_MOUSE_RIGHT_BUTTON_DOWN:
          case RawInputMouseButtonFlags.RI_MOUSE_RIGHT_BUTTON_UP:
          case RawInputMouseButtonFlags.RI_MOUSE_WHEEL:
            return false; //Reserve these events for navigation purposes
          case RawInputMouseButtonFlags.RI_MOUSE_MIDDLE_BUTTON_DOWN:
            buttonDown = true;
            id = 3;
            break;
          case RawInputMouseButtonFlags.RI_MOUSE_MIDDLE_BUTTON_UP:
            buttonUp = true;
            id = 3;
            break;
          case RawInputMouseButtonFlags.RI_MOUSE_BUTTON_4_DOWN:
            buttonDown = true;
            id = 4;
            break;
          case RawInputMouseButtonFlags.RI_MOUSE_BUTTON_4_UP:
            buttonUp = true;
            id = 4;
            break;
          case RawInputMouseButtonFlags.RI_MOUSE_BUTTON_5_DOWN:
            buttonDown = true;
            id = 5;
            break;
          case RawInputMouseButtonFlags.RI_MOUSE_BUTTON_5_UP:
            buttonUp = true;
            id = 5;
            break;
          default:
            return false; //Unsupported
        }
        name = $"Button{id}";
        code = id;
      }
      else if (hidEvent.IsGeneric)
      {
        if (hidEvent.Device == null)
        {
          LogEvent("Invalid device", hidEvent);
          return false;
        }
        long deviceId = (hidEvent.Device.VendorId << 16) | hidEvent.Device.ProductId;
        device = deviceId.ToString("X");
        long usageCategoryId = (hidEvent.UsagePage << 16) | hidEvent.UsageCollection;

        //Generic events with no usages are button up
        if (!hidEvent.Usages.Any() || buttonUp)
        {
          buttonUp = true;

          //Usage was saved from button down because its usage cannot be determined here
          if (!_genericKeyDownEvents.TryRemove(usageCategoryId, out var button))
          {
            bool handled = false;
            if (hidEvent.Device?.IsGamePad == true)
            {
              //Button down never happened so presume this is it if possible
              //because sometimes button down events are triggered as button up events
              var state = hidEvent.GetDirectionPadState();
              if (state != DirectionPadState.Rest)
              {
                name = $"Pad{state.ToString()}";
                code = -(long)state;
                buttonDown = true;
                handled = true;
              }
            }
            if (!handled)
            {
              //Some devices send duplicate button up events so ignore
              LogEvent("Unknown key", hidEvent);
              return false;
            }
          }
          else
          {
            name = button.Name;
            code = button.Code;
          }
        }
        else //Generic events with usages are button down
        {
          buttonDown = true;

          var id = hidEvent.Usages.FirstOrDefault();
          if (string.IsNullOrEmpty(device) || id == 0)
          {
            LogEvent("Invalid usage", hidEvent);
            return false;
          }

          //Some devices send duplicate button down events so ignore
          if (_genericKeyDownEvents.Values.Any(b => b.Code == id))
          {
            LogEvent("Duplicate key", hidEvent);
            return false;
          }

          if (hidEvent.Device?.IsGamePad == true)
          {
            var state = hidEvent.GetDirectionPadState();
            if (state != DirectionPadState.Rest)
            {
              name = $"Pad{state.ToString()}";
              code = -(long)state;
            }
            else if (buttonDown || buttonUp)
            {
              name = $"PadButton{id}";
              code = id;
            }
          }
          else if (hidEvent.UsagePageEnum == UsagePage.WindowsMediaCenterRemoteControl)
          {
            if (buttonDown || buttonUp)
            {
              string usage = id.ToString();
              if (Enum.IsDefined(typeof(RemoteButton), id))
                usage = Enum.GetName(typeof(RemoteButton), id);
              else if (Enum.IsDefined(typeof(SharpLib.Hid.Usage.WindowsMediaCenterRemoteControl), id))
                usage = Enum.GetName(typeof(SharpLib.Hid.Usage.WindowsMediaCenterRemoteControl), id);
              else if (Enum.IsDefined(typeof(SharpLib.Hid.Usage.HpWindowsMediaCenterRemoteControl), id))
                usage = Enum.GetName(typeof(SharpLib.Hid.Usage.HpWindowsMediaCenterRemoteControl), id);

              name = $"Remote{usage}";
              code = id;
            }
          }
          else if (hidEvent.UsagePageEnum == UsagePage.Consumer)
          {
            if (buttonDown || buttonUp)
            {
              string usage = id.ToString();
              if (Enum.IsDefined(typeof(SharpLib.Hid.Usage.ConsumerControl), id))
                usage = Enum.GetName(typeof(SharpLib.Hid.Usage.ConsumerControl), id);
              name = $"{usage}";
              code = id;
            }
          }
          else if (hidEvent.UsagePageEnum == UsagePage.GameControls)
          {
            if (buttonDown || buttonUp)
            {
              string usage = id.ToString();
              if (Enum.IsDefined(typeof(SharpLib.Hid.Usage.GameControl), id))
                usage = Enum.GetName(typeof(SharpLib.Hid.Usage.GameControl), id);
              name = $"{usage}";
              code = id;
            }
          }
          else if (hidEvent.UsagePageEnum == UsagePage.SimulationControls)
          {
            if (buttonDown || buttonUp)
            {
              string usage = id.ToString();
              if (Enum.IsDefined(typeof(SharpLib.Hid.Usage.SimulationControl), id))
                usage = Enum.GetName(typeof(SharpLib.Hid.Usage.SimulationControl), id);
              name = $"{usage}";
              code = id;
            }
          }
          else if (hidEvent.UsagePageEnum == UsagePage.Telephony)
          {
            if (buttonDown || buttonUp)
            {
              string usage = id.ToString();
              if (Enum.IsDefined(typeof(SharpLib.Hid.Usage.TelephonyDevice), id))
                usage = Enum.GetName(typeof(SharpLib.Hid.Usage.TelephonyDevice), id);
              name = $"{usage}";
              code = id;
            }
          }
          else
          {
            if (buttonDown || buttonUp)
            {
              name = $"Event{id}";
              code = id;
            }
          }
        }
        if (buttonDown)
        {
          //Save so it can be used for button up
          _genericKeyDownEvents.TryAdd(usageCategoryId, (name, code));
        }
      }
      else
      {
        LogEvent("Unsupported", hidEvent);
        return false;
      }

      return true;
    }

    private static bool KeyCombinationsMatch(IEnumerable<long> keyMapping, IEnumerable<long> pressedKeys)
    {
      if (keyMapping.Count() == 0 || pressedKeys.Count() == 0)
        return false;

      return keyMapping.All(c => pressedKeys.Contains(c)) && pressedKeys.All(c => keyMapping.Contains(c));
    }

    /// <summary>
    /// Windows message key and HID key might be different for the same key, so convert them to the same base key if needed
    /// </summary>
    private static Keys ConvertSystemKey(Keys key)
    {
      if (key == Keys.LControlKey || key == Keys.RControlKey || key == Keys.Control)
        return Keys.ControlKey;
      if (key == Keys.LMenu || key == Keys.RMenu || key == Keys.Alt)
        return Keys.Menu;
      if (key == Keys.LShiftKey || key == Keys.RShiftKey || key == Keys.Shift)
        return Keys.ShiftKey;

      return key;
    }

    private static bool TryExecuteDefaultAction(long code, SharpLib.Hid.Event hidEvent)
    {
      if (_defaultRemoteKeyCodes.TryGetValue(hidEvent.UsagePageEnum, out var keyDic) && keyDic.TryGetValue(code, out Key key))
      {
        ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Executing default key {0} action: {1}", code, key);
        if (key != null && key != Key.None && (_navigationKeys.Any(k => k == key) || _externalKeyPressHandlers.Count == 0)) //Only allow navigation key during external handling
        {
          ServiceRegistration.Get<IInputManager>().KeyPress(key);
          return true;
        }
      }
      if (_defaultRemoteScreenCodes.TryGetValue(hidEvent.UsagePageEnum, out var screenDic) && screenDic.TryGetValue(code, out string screen))
      {
        if (screen.StartsWith(InputDeviceModel.HOME_PREFIX, StringComparison.InvariantCultureIgnoreCase))
        {
          var homeScreen = screen.Replace(InputDeviceModel.HOME_PREFIX, "");
          ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Executing default home action: " + homeScreen);
          NavigateToScreen(homeScreen);
          return true;
        }
        else if (screen.StartsWith(InputDeviceModel.CONFIG_PREFIX, StringComparison.InvariantCultureIgnoreCase))
        {
          var configScreen = screen.Replace(InputDeviceModel.CONFIG_PREFIX, "");
          ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Executing defauly config action: " + configScreen);
          NavigateToScreen(configScreen, InputDeviceModel.CONFIGURATION_STATE_ID);
          return true;
        }
      }
      return false;
    }

    private static bool IsModifierKey(long code)
    {
      return code == (long)Keys.ControlKey || code == (long)Keys.Menu || code == (long)Keys.ShiftKey || code == (long)Keys.RWin || code == (long)Keys.LWin;
    }

    /// <summary>
    /// Reset timer so key presses will be reset after timeout in case focus is lost and key up events are never received
    /// </summary>
    private static void RestartKeyPressedTimer()
    {
      _pressedKeyTimer.Stop();
      _pressedKeyTimer.Start();
    }

    private static void AddPressedKey(string name, long code)
    {
      if (_pressedKeys.Values.Any(c => !IsModifierKey(c)) && !IsModifierKey(code))
        ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Invalid key {0} in combination with {1}", name, string.Join(", ", _pressedKeys.Keys));
      else
        _pressedKeys.TryAdd(name, code);

      RestartKeyPressedTimer();
    }

    private static void RemovePressedKey(string name)
    {
      _pressedKeys.TryRemove(name, out _);
    }

    private static void OnHidEvent(object sender, SharpLib.Hid.Event hidEvent)
    {
      try
      {
        if (CAPTURE_ONLY_IN_FOREGROUND && hidEvent.IsBackground)
          return;

        if (!hidEvent.IsValid)
        {
          ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: HID Event Invalid");
          return;
        }

        if (!TryDecodeEvent(hidEvent, out string type, out string name, out long code, out bool buttonUp, out bool buttonDown))
          return;

        if (buttonDown)
          AddPressedKey(name, code);

        InputDevice device;
        bool keyHandled = false;
        bool handleKeyPress = (SUPPORT_REPEATS && buttonDown) || (!SUPPORT_REPEATS && buttonUp);
        if (_inputDevices.TryGetValue(type, out device))
        {
          ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Checking mapping for device: " + device.Name);
          ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Checking keys: " + string.Join(", ", _pressedKeys.Select(k => k.Key)));
          ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Checking codes: " + string.Join(", ", _pressedKeys.Select(k => k.Value)));

          var keyMappings = device.KeyMap.Where(m => KeyCombinationsMatch(m.Codes.Select(c => c.Code), _pressedKeys.Values));
          if (keyMappings?.Count() > 0)
          {
            ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Found matching mappings: " + keyMappings.Count());
            ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Button states: {0}/{1}", buttonDown, buttonUp);

            //_currentMessage.Result = new IntPtr(1);
            _currentMessageHandled = true;
            if (handleKeyPress)
            {
              foreach (var keyMapping in keyMappings)
              {
                string[] actionArray = keyMapping.Key.Split('.');
                if (actionArray.Length >= 2)
                {
                  if (keyMapping.Key.StartsWith(InputDeviceModel.KEY_PREFIX, StringComparison.InvariantCultureIgnoreCase))
                  {
                    if (_navigationKeys.Any(k => k.Name == actionArray[1]) || _externalKeyPressHandlers.Count == 0) //Only allow navigation key during external handling
                    {
                      ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Executing key action: " + actionArray[1]);
                      ServiceRegistration.Get<IInputManager>().KeyPress(Key.GetSpecialKeyByName(actionArray[1]));
                      keyHandled = true;
                    }
                  }
                  else if (_externalKeyPressHandlers.Count == 0) //Don't interfere with external handlers by executing screen changes
                  {
                    if (keyMapping.Key.StartsWith(InputDeviceModel.HOME_PREFIX, StringComparison.InvariantCultureIgnoreCase))
                    {
                      ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Executing home action: " + actionArray[1]);
                      NavigateToScreen(actionArray[1]);
                      keyHandled = true;
                    }
                    else if (keyMapping.Key.StartsWith(InputDeviceModel.CONFIG_PREFIX, StringComparison.InvariantCultureIgnoreCase))
                    {
                      ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Executing config action: " + actionArray[1]);
                      NavigateToScreen(actionArray[1], InputDeviceModel.CONFIGURATION_STATE_ID);
                      keyHandled = true;
                    }
                  }
                }
              }
            }
          }
        }

        //Check if default handling is available for unhandled single key presses from non-keyboard devices
        if (!keyHandled && _pressedKeys.Count == 1 && hidEvent.IsGeneric && handleKeyPress)
        {
          ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Checking default keys: " + string.Join(", ", _pressedKeys.Select(k => k.Key)));
          ServiceRegistration.Get<ILogger>().Debug("InputDeviceManager: Checking default codes: " + string.Join(", ", _pressedKeys.Select(k => k.Value)));

          if (TryExecuteDefaultAction(_pressedKeys.Values.First(), hidEvent))
            keyHandled = true;
        }

        if (_externalKeyPressHandlers.Count > 0)
        {
          if (buttonDown && !hidEvent.IsRepeat) //Only send button down and no repeats for better consitency
          {
            lock (_listSyncObject)
            {
              foreach (var action in _externalKeyPressHandlers)
                action.Invoke(sender, hidEvent, type, _pressedKeys);
            }
          }
          _currentMessageHandled = true;
        }

        if (buttonUp)
          RemovePressedKey(name);
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Error("InputDeviceManager: HID event failed", ex);
      }
    }

    private static bool NavigateToScreen(string name, Guid? requiredState = null)
    {
      IWorkflowManager workflowManager = ServiceRegistration.Get<IWorkflowManager>();
      if (workflowManager != null)
      {
        if (requiredState.HasValue && workflowManager.CurrentNavigationContext.WorkflowState.StateId != requiredState.Value)
          workflowManager.NavigatePush(requiredState.Value);

        foreach (NavigationContext context in workflowManager.NavigationContextStack.ToList())
        {
          var action = context.MenuActions.Values.FirstOrDefault(a => a.Name == name);
          if (action != null)
          {
            action.Execute();
            return true;
          }
        }
      }
      return false;
    }

    #endregion

    #region Settings

    private ICollection<RemoteKeyCode> LoadRemoteMap(string remoteFile)
    {
      if (!File.Exists(remoteFile))
        return new List<RemoteKeyCode>();

      XmlSerializer reader = new XmlSerializer(typeof(List<RemoteKeyCode>));
      using (StreamReader file = new StreamReader(remoteFile))
        return (ICollection<RemoteKeyCode>)reader.Deserialize(file);
    }

    private void LoadDefaulRemoteMaps(PluginRuntime pluginRuntime)
    {
      _defaultRemoteKeyCodes[UsagePage.WindowsMediaCenterRemoteControl] = new Dictionary<long, Key>();
      _defaultRemoteKeyCodes[UsagePage.Consumer] = new Dictionary<long, Key>();
      _defaultRemoteScreenCodes[UsagePage.WindowsMediaCenterRemoteControl] = new Dictionary<long, string>();
      _defaultRemoteScreenCodes[UsagePage.Consumer] = new Dictionary<long, string>();
      var keyCodes = LoadRemoteMap(pluginRuntime.Metadata.GetAbsolutePath("DefaultRemoteMap.xml"));
      foreach (RemoteKeyCode mkc in keyCodes)
      {
        if (mkc.FuncName.StartsWith("S:") || mkc.FuncName.StartsWith("P:"))
          _defaultRemoteKeyCodes[UsagePage.WindowsMediaCenterRemoteControl][mkc.Code] = Key.DeserializeKey(mkc.FuncName);
        else
          _defaultRemoteScreenCodes[UsagePage.WindowsMediaCenterRemoteControl][mkc.Code] = mkc.FuncName;
      }
      keyCodes = LoadRemoteMap(pluginRuntime.Metadata.GetAbsolutePath("DefaultConsumerRemoteMap.xml"));
      foreach (RemoteKeyCode mkc in keyCodes)
      {
        if (mkc.FuncName.StartsWith("S:") || mkc.FuncName.StartsWith("P:"))
          _defaultRemoteKeyCodes[UsagePage.Consumer][mkc.Code] = Key.DeserializeKey(mkc.FuncName);
        else
          _defaultRemoteScreenCodes[UsagePage.Consumer][mkc.Code] = mkc.FuncName;
      }
    }

    private void LoadSettings()
    {
      ISettingsManager settingsManager = ServiceRegistration.Get<ISettingsManager>();
      InputManagerSettings settings = settingsManager.Load<InputManagerSettings>();

      UpdateLoadedSettings(settings);
    }

    /// <summary>
    /// This function updates the local variable "_inputDevices"
    /// </summary>
    /// <param name="settings"></param>
    public void UpdateLoadedSettings(InputManagerSettings settings)
    {
      _inputDevices.Clear();
      if (settings != null && settings.InputDevices != null)
        try
        {
          foreach (InputDevice device in settings.InputDevices)
            _inputDevices.TryAdd(device.Type, device);
        }
        catch
        {
          // ignored
        }
    }

    #endregion

    #region External event handling

    public bool RegisterExternalKeyHandling(Action<object, SharpLib.Hid.Event, string, IDictionary<string, long>> hidEvent)
    {
      lock (_listSyncObject)
      {
        if (!_externalKeyPressHandlers.Contains(hidEvent))
        {
          _externalKeyPressHandlers.Add(hidEvent);
          return true;
        }
      }
      return false;
    }

    public bool UnRegisterExternalKeyHandling(Action<object, SharpLib.Hid.Event, string, IDictionary<string, long>> hidEvent)
    {
      lock (_listSyncObject)
      {
        if (_externalKeyPressHandlers.Contains(hidEvent))
        {
          _externalKeyPressHandlers.Remove(hidEvent);
          return true;
        }
      }
      return false;
    }

    public void RemoveAllExternalKeyHandling()
    {
      lock (_listSyncObject)
      {
        _externalKeyPressHandlers.Clear();
      }
    }

    #endregion

    #region Implementation of IPluginStateTracker

    /// <summary>
    /// Will be called when the plugin is started. This will happen as a result of a plugin auto-start
    /// or an item access which makes the plugin active.
    /// This method is called after the plugin's state was set to <see cref="PluginState.Active"/>.
    /// </summary>
    public void Activated(PluginRuntime pluginRuntime)
    {
      LoadSettings();
      LoadDefaulRemoteMaps(pluginRuntime);
      SubscribeToMessages();
      var thread = new Thread(StartThread);
      thread.Start();
    }

    /// <summary>
    /// Schedules the stopping of this plugin. This method returns the information
    /// if this plugin can be stopped. Before this method is called, the plugin's state
    /// will be changed to <see cref="PluginState.EndRequest"/>.
    /// </summary>
    /// <remarks>
    /// This method is part of the first phase in the two-phase stop procedure.
    /// After this method returns <c>true</c> and all item's clients also return <c>true</c>
    /// as a result of their stop request, the plugin's state will change to
    /// <see cref="PluginState.Stopping"/>, then all uses of items by clients will be canceled,
    /// then this plugin will be stopped by a call to method <see cref="IPluginStateTracker.Stop"/>.
    /// If either this method returns <c>false</c> or one of the items clients prevent
    /// the stopping, the plugin will continue to be active and the method <see cref="IPluginStateTracker.Continue"/>
    /// will be called.
    /// </remarks>
    /// <returns><c>true</c>, if this plugin can be stopped at this time, else <c>false</c>.
    /// </returns>
    public bool RequestEnd()
    {
      return true;
    }

    /// <summary>
    /// Second step of the two-phase stopping procedure. This method stops this plugin,
    /// i.e. removes the integration of this plugin into the system, which was triggered
    /// by the <see cref="IPluginStateTracker.Activated"/> method.
    /// </summary>
    public void Stop()
    {
      UnsubscribeFromMessages();
      if (_hidHandler != null)
      {
        //First de-register
        _hidHandler.Dispose();
        _hidHandler = null;
      }
    }

    /// <summary>
    /// Revokes the end request which was triggered by a former call to the
    /// <see cref="IPluginStateTracker.RequestEnd"/> method and restores the active state. After this call, the plugin remains active as
    /// it was before the call of <see cref="IPluginStateTracker.RequestEnd"/> method.
    /// </summary>
    public void Continue()
    {
    }

    /// <summary>
    /// Will be called before the plugin manager shuts down. The plugin can perform finalization
    /// tasks here. This method will called independently from the plugin state, i.e. it will also be called when the plugin
    /// was disabled or not started at all.
    /// </summary>
    public void Shutdown()
    {
    }

    #endregion
  }
}
