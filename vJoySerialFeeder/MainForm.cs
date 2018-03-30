﻿/*
 * Created by SharpDevelop.
 * User: Cleric
 * Date: 8.6.2017 г.
 * Time: 16:58 ч.
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Windows.Forms;

using MoonSharp.Interpreter;

namespace vJoySerialFeeder
{
	/// <summary>
	/// Description of MainForm.
	/// </summary>
	public partial class MainForm : Form
	{
		public static MainForm Instance {get; private set; }
		
		public int[] Channels { get; private set; }
		public int ActiveChannels { get ; private set; }
		public VJoyBase VJoy { get; private set;}
		
		public event EventHandler ChannelDataUpdate;
		
		public int MappingCount { get { return mappings.Count; } }
		
		private List<Mapping> mappings = new List<Mapping>();
		public Mapping MappingAt(int i) { return i >= mappings.Count ? null : mappings[i]; }
		private bool connected = false;
		private SerialReader serialReader;
		private string protocolConfig = "";
		
		private VJoyCollectionBase vJoyEnumerator;
		
		private Configuration config;
		private Configuration.Profile currentProfile;
		private bool useCustomSerialParameters;

		private Configuration.SerialParameters serialParameters;
		
		private double updateRate;
		
		private Type[] Protocols = {typeof(IbusReader), typeof(MultiWiiReader), typeof(SbusReader), typeof(DummyReader)};
		
		private ComAutomation comAutomation;
		private WebSocket webSocket;
		
		private Lua lua;
		private string luaScript;
		
		private LuaOutputForm luaOutputDialog = new LuaOutputForm();
		private MonitorForm monitorForm = new MonitorForm();
		
		public MainForm(string[] args)
		{
			//
			// The InitializeComponent() call is required for Windows Forms designer support.
			//
			InitializeComponent();
			
			Instance = this;
			 
			Channels = new int[256];

            switch (Environment.OSVersion.Platform) {
                case PlatformID.Win32NT:
					vJoyEnumerator = (VJoyCollectionBase)Activator.CreateInstance(Type.GetType("vJoySerialFeeder.VJoyCollectionWindows"));
                    break;
                case PlatformID.Unix:
                    vJoyEnumerator = (VJoyCollectionBase)Activator.CreateInstance(Type.GetType("vJoySerialFeeder.VJoyCollectionLinux"));
                    break;
                default:
                    ErrorMessageBox("Unsupported platform", "Fatal");
                    Application.Exit();
                    break;
            }

            comboPorts.FormattingEnabled = true;
            comboPorts.Format += (o, e) =>
            {
                // strip /dev/ on Linux, to be more compact
                e.Value = e.Value.ToString().Replace("/dev/", "");
            };
			reloadComPorts();
			reloadJoysticks();
			ChannelDataUpdate += onChannelDataUpdate;

			config = Configuration.Load();
			
			reloadProfiles();
			
			var defaultProfile = config.GetProfile(config.DefaultProfile);
			if(defaultProfile == null && comboProfiles.Items.Count > 0) {
				var first = comboProfiles.Items[0].ToString();
				defaultProfile = config.GetProfile(first);
				comboProfiles.Text = first;
			}
			
			if(defaultProfile != null) {
				comboProfiles.Text = config.DefaultProfile;
				loadProfile(defaultProfile);
			}
			else
				resetProfile();
			
			toolStripStatusLabel.Text = "Disconnected";
			
			// initialize COM on windows platforms
			if(System.Environment.OSVersion.Platform == PlatformID.Win32NT)
				comAutomation = ComAutomation.GetInstance();
			
			// initialize websocket if configured
			StartStopWebSocket();
		}
		
		/// <summary>
		/// Called from the Mapping class when the mapping should remove
		/// itself from the MainForm
		/// </summary>
		/// <param name="m"></param>
		public void RemoveMapping(Mapping m) {
			panelMappings.SuspendLayout();
			panelMappings.Controls.Remove(m.GetControl().Parent);
			mappings.Remove(m);
			reEnumerateMappings();
			panelMappings.ResumeLayout();
		}
		
		void addMapping(Mapping m) {
			var fp = new FlowLayoutPanel();
			fp.AutoSize = true;
			fp.FlowDirection = FlowDirection.LeftToRight;
			fp.WrapContents = false;
			var label = new Label();
			label.Size = new Size(30, 20);
			label.TextAlign = ContentAlignment.BottomLeft;
			fp.Controls.Add(label);
			fp.Controls.Add(m.GetControl());
			mappings.Add(m);
			
			panelMappings.SuspendLayout();
			panelMappings.Controls.Add(fp);
			reEnumerateMappings();
			panelMappings.ResumeLayout();
		}
		
		void reEnumerateMappings() {
			var i = 1;
			foreach(FlowLayoutPanel c in panelMappings.Controls) {
				var l = c.Controls[0] as Label;
				l.Text = i++ + ")";
			}
		}
		
		
		
		
		
		void ErrorMessageBox(string message, string title) {
			MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
			
		private void loadProfile(Configuration.Profile p) {
			lua = null;
			
			while(mappings.Count > 0)
				mappings[0].Remove();
			
			if(!connected) {
				// load this stuff only if not connected
				comboProtocol.SelectedIndex = p.Protocol < comboProtocol.Items.Count ? p.Protocol : 0;
                comboPorts.SelectedItem = p.COMPort;
                if (comboPorts.SelectedItem == null && comboPorts.Items.Count > 0)
                    comboPorts.SelectedIndex = 0;
				useCustomSerialParameters = p.UseCustomSerialParameters;
				serialParameters = p.SerialParameters;
				protocolConfig = p.ProtocolConfiguration;
				comboJoysticks.SelectedItem = p.VJoyInstance;
				if(comboJoysticks.SelectedItem == null && comboJoysticks.Items.Count > 0)
					comboJoysticks.SelectedIndex = 0;
			}
			
			foreach(var m in p.Mappings) {
				addMapping(m.Copy());
			}
			
			luaScript = p.LuaScript;
			lua = new Lua(luaScript);
			
			currentProfile = p;
		}
		
		private Configuration.Profile buildProfile() {
			var p = new Configuration.Profile();
				
			p.Protocol = comboProtocol.SelectedIndex;
            p.COMPort = (string)comboPorts.SelectedItem;
			p.UseCustomSerialParameters = useCustomSerialParameters;
			p.SerialParameters = serialParameters;
			p.ProtocolConfiguration = protocolConfig;
			p.VJoyInstance = comboJoysticks.Text;
			p.LuaScript = luaScript;

			p.Mappings = new List<Mapping>();
			
			foreach (var m in mappings)
				p.Mappings.Add(m.Copy());
			
			return p;
		}
		
		private void reloadProfiles() {
			var ps = config.GetProfileNames();
			Array.Sort(ps);
			comboProfiles.Items.Clear();
			comboProfiles.Items.AddRange(ps);
		}
		
		private void resetProfile() {
			// reset profile
			loadProfile(new Configuration.Profile());
			comboProfiles.Text = config.DefaultProfile = "";
			currentProfile = buildProfile();
		}
		
		private void reloadComPorts() {
			object prevPort = comboPorts.SelectedItem;
			comboPorts.Items.Clear();
            comboPorts.Items.AddRange(SerialPort.GetPortNames());
			comboPorts.SelectedItem = prevPort;
			if(comboPorts.SelectedItem == null && comboPorts.Items.Count > 0)
				comboPorts.SelectedIndex = 0;
		}
		
		private void reloadJoysticks() {
			object prevJoy = comboJoysticks.SelectedItem;
			comboJoysticks.Items.Clear();
			comboJoysticks.Items.AddRange(vJoyEnumerator.GetJoysticks());
			comboJoysticks.SelectedItem = prevJoy;
			if(comboJoysticks.SelectedItem == null && comboJoysticks.Items.Count > 0)
				comboJoysticks.SelectedIndex = 0;
		}
		
		private SerialReader createSerialReader() {
			return (SerialReader)Activator.CreateInstance(Protocols[comboProtocol.SelectedIndex]);
		}
		
		private void connect() {
			if(comboJoysticks.SelectedItem != null) {
				try {
					VJoy = vJoyEnumerator.GetVJoy(comboJoysticks.SelectedItem.ToString());
				}
				catch(VJoyBase.VJoyException ex) {
					ErrorMessageBox(ex.Message, "VJoy Error");
					return;
				}
			}

			serialReader = createSerialReader();
			
			var sp = useCustomSerialParameters ?
				serialParameters
				: serialReader.GetDefaultSerialParameters();

            if(!serialReader.OpenPort((string)comboPorts.SelectedItem, sp)) {
                ErrorMessageBox("Can not open the port", "Serial Error");
                VJoy.Release();
                return;
            }

			comboProtocol.Enabled = false;
			comboPorts.Enabled = false;
			buttonPortSetup.Enabled = false;
			buttonPortsRefresh.Enabled = false;
			buttonProtocolSetup.Enabled = false;
			buttonConnect.Text = "Disconnect";
			comboJoysticks.Enabled = false;
			connected = true;
			
			lua = new Lua(luaScript);
			
			backgroundWorker.RunWorkerAsync();
		}
		
		private void disconnect() {
			// when the background worker finished disconnect2 is called
			buttonConnect.Text = "Disconnecting";
			backgroundWorker.CancelAsync();
		}
		
		private void disconnect2() {
			VJoy.Release();
			try {
                serialReader.ClosePort();
			} catch(Exception) {}
			
			ActiveChannels = 0;
			comboProtocol.Enabled = true;
			comboPorts.Enabled = true;
			buttonPortSetup.Enabled = true;
			buttonPortsRefresh.Enabled = true;
			buttonProtocolSetup.Enabled = true;
			buttonConnect.Text = "Connect";
			connected = false;
			toolStripStatusLabel.Text = "Disconnected";
			comboJoysticks.Enabled = true;
		}
		
		void onChannelDataUpdate(object sender, EventArgs e) {
			if(!ContainsFocus) return;
			foreach(var mapping in mappings) {
				mapping.Paint();
			}
			toolStripStatusLabel.Text = "Connected, "+ActiveChannels
				+" channels available, "+Math.Round(updateRate)+" Updates per second / "
				+ (updateRate < 0.001 ? "∞" : Math.Round(1000/updateRate).ToString()) + " ms between Updates";
		}
		
		void StartStopWebSocket() {
			if(config.WebSocketEnabled && webSocket == null) {
				try {
					webSocket = new WebSocket(config.WebSocketPort);
				}
				catch(SocketException ex) {
					if(ex.SocketErrorCode == SocketError.AddressAlreadyInUse) {
						ErrorMessageBox("Port already in use!", "WebSocket Listener");
					}
					else
						ErrorMessageBox(ex.Message, "WebSocket Listener");
					webSocket = null;
					config.WebSocketEnabled = false;
					config.Save();
				}
			}
			else if(!config.WebSocketEnabled && webSocket != null) {
				webSocket.Stop();
				webSocket = null;
			}
		}
		
		
		
		
		
		
		
		
		
		
		
		#region Background Worker for doing the main job
		
		void BackgroundWorkerDoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
		{
			try {
				serialReader.Init(Channels, protocolConfig);
				serialReader.Start();
				
				double nextUIUpdateTime = 0, nextRateUpdateTime = 0, prevTime = 0;
				double updateSum = 0;
				int updateCount = 0;
				
				while(true) {
					if(backgroundWorker.CancellationPending) {
						e.Cancel = true;
						serialReader.Stop();
						return;
					}
					
					try {
						ActiveChannels = serialReader.ReadChannels();
					}
					catch(InvalidOperationException ex) {
						System.Diagnostics.Debug.WriteLine(ex.Message);
						this.Invoke((Action)( () => ErrorMessageBox("The Serial Port was Disconnected!",
						                                            "Disconnect")));
						backgroundWorker.CancelAsync();
						continue;
					}
					catch(Exception ex) {
						ActiveChannels = 0;
						System.Diagnostics.Debug.WriteLine(ex.Message);
					}
					if(ActiveChannels > 0) {
						foreach(Mapping m in mappings) {
							if(m.Channel >= 0 && m.Channel < ActiveChannels)
								m.Input = Channels[m.Channel];
						}
						
						try {
							lua.Update(VJoy, Channels);
						}
						catch(NullReferenceException) {}
						catch(InterpreterException ex) {
							this.Invoke((Action)( () =>
							            ErrorMessageBox("Lua script execution failed. Scripting disabled:\n\n" + ex.DecoratedMessage,
							                  "Lua Error")));
						}
						
						foreach(Mapping m in mappings) {
							m.UpdateJoystick(VJoy);
						}
						
						VJoy.SetState();
						
						if(comAutomation != null)
							comAutomation.Dispatch();
						
						if(webSocket != null)
							webSocket.Dispatch();
					}
					
					
					double now = (double)DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
					
					// since the time between frames may vary we sum the times here
					// and later publish the average
					if(now > prevTime && ActiveChannels > 0) {
						updateSum += 1000.0/(now - prevTime);
						updateCount++;
					}
					
					// update UI on every 100ms
					if(now >= nextUIUpdateTime) {
						nextUIUpdateTime = now + 100;
						
						// update the Rate on evert 500ms
						if(now >= nextRateUpdateTime) {
							nextRateUpdateTime = now + 500;
							
							if(ActiveChannels == 0)
								updateRate = 0;
							else if(updateCount > 0) {
								updateRate = updateSum/updateCount;
								updateSum = updateCount = 0;
							}
						}
						
						// will emit the ChannelDataUpdate event on the UI thread
						backgroundWorker.ReportProgress(0);
					}
					
					if(ActiveChannels > 0)
						prevTime = now;
				}
			}
			catch(Exception ex) {
				this.Invoke((Action)( () =>
				                     ErrorMessageBox(ex.ToString(), "Main Worker")));
			}
		}
		void BackgroundWorkerRunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
		{
			disconnect2();
		}
		
		void BackgroundWorkerProgressChanged(object sender, System.ComponentModel.ProgressChangedEventArgs e)
		{
			ChannelDataUpdate(this, e);
		}
		
		#endregion
		
		
		
		
		
		
		
		
		
		#region UI Event handlers
	
		void ButtonAddAxisClick(object sender, EventArgs e)
		{
			addMapping(new AxisMapping());
		}

		void ButtonAddButtonClick(object sender, EventArgs e)
        {
			addMapping(new ButtonMapping());
        }
		
		void ButtonBitmappedButtonClick(object sender, EventArgs e)
        {
			addMapping(new ButtonBitmapMapping());
        }

		
		void FlowLayoutPanel1MouseEnter(object sender, EventArgs e)
		{
			// trick to make mouse wheel scroll possible without
			// explicitly focusing on the panel
			if(ContainsFocus)
				panelMappings.Focus();
		}
		void ButtonPortsRefreshClick(object sender, EventArgs e)
		{
			reloadComPorts();
		}
		
		void ButtonConnectClick(object sender, EventArgs e)
		{
			if(!connected)
				connect();
			else
				disconnect();
		}
		
		void ButtonSaveProfileClick(object sender, EventArgs e)
		{
			string name = comboProfiles.Text.Trim();
			if(name.Length == 0) {
				MessageBox.Show("Enter a profile name");
				return;
			}			
			
			var p = buildProfile();
			
			config.PutProfile(name, p);
			config.DefaultProfile = name;
			config.Save();
			
			currentProfile = p;
			
			reloadProfiles();
		}
		
		void ButtonLoadProfileClick(object sender, EventArgs ea)
		{
			string name = comboProfiles.Text.Trim();
			if(name.Length == 0) {
				MessageBox.Show("Enter a profile name");
				return;
			}
			var p = config.GetProfile(name);
			if(p == null) {
				MessageBox.Show("No such profile");
				return;
			}
			
			if(!Configuration.ProfilesEqual(buildProfile(), currentProfile)) {
        		var res = MessageBox.Show("There are unsaved changes in your Profile! If you load the requested profile the changes will be lost. Continue with loading?",
        		             "Profile not saved", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
				if(res == DialogResult.No)
				    return;
        	}
			
			loadProfile(p);
			
			config.DefaultProfile = name;
			config.Save();
		}
		
		void ButtonDeleteProfileClick(object sender, EventArgs e)
		{
			string name = comboProfiles.Text.Trim();
			if(name.Length == 0) {
				MessageBox.Show("Enter a profile name");
				return;
			}
			config.DeleteProfile(name);
			config.Save();
			reloadProfiles();
		}
        
        void ButtonPortSetupClick(object sender, EventArgs e)
        {
        	var sp = useCustomSerialParameters ?
        		serialParameters
        		: createSerialReader().GetDefaultSerialParameters();
        	var d = new PortSetupForm(useCustomSerialParameters, sp);
        	d.ShowDialog();
        	if(d.DialogResult == DialogResult.OK) {
        		useCustomSerialParameters = d.UseCustomSerialParameters;
        		serialParameters = d.SerialParameters;
        	}
        }
        
        void ButtonProtocolSetupClick(object sender, EventArgs e)
        {
        	var c = createSerialReader().Configure(protocolConfig);
        	if(c != null)
        		protocolConfig = c;
        }
        
        void ComboProtocolSelectedIndexChanged(object sender, EventArgs e)
        {
        	buttonProtocolSetup.Visible = createSerialReader().Configurable;
        	protocolConfig = "";
        }
        
        void MainFormFormClosing(object sender, FormClosingEventArgs e)
        {
        	// check if profile needs saving
        	if(!Configuration.ProfilesEqual(buildProfile(), currentProfile)) {
        		var res = MessageBox.Show("There are unsaved changes in your Profile! Are you sure you want to quit?",
        		             "Profile not saved", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
				if(res == DialogResult.No)
				    e.Cancel = true;
        	}
        }
        
        void ButtonNewProfileClick(object sender, EventArgs e)
        {
        	if(!Configuration.ProfilesEqual(buildProfile(), currentProfile)) {
        		var res = MessageBox.Show("There are unsaved changes in your Profile! Are you sure you want to discard them?",
        		             "Profile not saved", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        		if(res == DialogResult.No) {
        			return;
        		}
				    
        	}
        	
        	resetProfile();
        }
        
        
        
        
        void ChannelMonitorMenuClick(object sender, EventArgs e)
        {
        	if(!monitorForm.Visible)
	        	monitorForm.Show();
        	else
        		monitorForm.Hide();
        }
        
        void OptionsMenuClick(object sender, EventArgs e)
        {
        	using(var d = new OptionsForm(config.WebSocketEnabled, config.WebSocketPort)) {
        		d.ShowDialog();
        		if(d.DialogResult == DialogResult.OK) {
        			if(config.WebSocketEnabled && d.WebSocketEnabled
        			   && config.WebSocketPort != d.WebSocketPort) {
        				// changing port
        				config.WebSocketEnabled = false;
        				StartStopWebSocket(); // stop
        				
        				config.WebSocketEnabled = true;
        				config.WebSocketPort = d.WebSocketPort;
        				StartStopWebSocket(); // start
        			}
        			else {
	        			config.WebSocketEnabled = d.WebSocketEnabled;
	        			config.WebSocketPort = d.WebSocketPort;
	        			StartStopWebSocket();
        			}
        			config.Save();
        		}
        	}
        }
        
        void ExitMenuClick(object sender, EventArgs e)
        {
        	Close();
        }
        
        void ScriptEditMenuClick(object sender, EventArgs e)
        {
        	using(var d = new LuaEditorForm(luaScript)) {
        		d.ShowDialog();
        		
        		if(d.DialogResult == DialogResult.OK) {
        			luaScript = d.ScriptSource;
        			lua = new Lua(luaScript);
        		}
        	}
        }
        
        void ScriptOutputMenuClick(object sender, EventArgs e)
        {
        	luaOutputDialog.Visible = !luaOutputDialog.Visible;
        }
        
        void ManualMenuClick(object sender, EventArgs e)
        {
        	System.Diagnostics.Process.Start("https://github.com/Cleric-K/vJoySerialFeeder/blob/master/Docs/MANUAL.md");
        }
        
        void MenuStrip1MenuActivate(object sender, EventArgs e)
        {
        	outputToolStripMenuItem.Checked = luaOutputDialog.Visible;
        	channelMonitorToolStripMenuItem.Checked = monitorForm.Visible;
        }
        
        #endregion
	}
}
