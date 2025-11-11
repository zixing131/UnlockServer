namespace UnlockServer
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.btn_save = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.txtip = new System.Windows.Forms.TextBox();
            this.txtpt = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.txtus = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.txtpd = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.txtrssi = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.notifyIcon1 = new System.Windows.Forms.NotifyIcon(this.components);
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.显示ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.隐藏ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.锁屏ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.退出ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.label7 = new System.Windows.Forms.Label();
            this.rdbclassic = new System.Windows.Forms.RadioButton();
            this.rdbble = new System.Windows.Forms.RadioButton();
            this.button2 = new System.Windows.Forms.Button();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.label8 = new System.Windows.Forms.Label();
            this.ckb_autolock = new System.Windows.Forms.CheckBox();
            this.ckb_autounlock = new System.Windows.Forms.CheckBox();
            this.ckb_manuallock = new System.Windows.Forms.CheckBox();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.ckb_manuclunlock = new System.Windows.Forms.CheckBox();
            this.button3 = new System.Windows.Forms.Button();
            this.btn_searchDevice = new System.Windows.Forms.Button();
            this.txt_bleDevice = new System.Windows.Forms.TextBox();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // btn_save
            // 
            this.btn_save.Location = new System.Drawing.Point(82, 774);
            this.btn_save.Margin = new System.Windows.Forms.Padding(6);
            this.btn_save.Name = "btn_save";
            this.btn_save.Size = new System.Drawing.Size(176, 82);
            this.btn_save.TabIndex = 0;
            this.btn_save.Text = "保存配置";
            this.btn_save.UseVisualStyleBackColor = true;
            this.btn_save.Click += new System.EventHandler(this.btn_save_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(78, 64);
            this.label1.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(106, 24);
            this.label1.TabIndex = 1;
            this.label1.Text = "电脑IP：";
            // 
            // txtip
            // 
            this.txtip.Location = new System.Drawing.Point(220, 58);
            this.txtip.Margin = new System.Windows.Forms.Padding(6);
            this.txtip.Name = "txtip";
            this.txtip.Size = new System.Drawing.Size(820, 35);
            this.txtip.TabIndex = 2;
            this.txtip.Text = "127.0.0.1";
            // 
            // txtpt
            // 
            this.txtpt.Location = new System.Drawing.Point(220, 146);
            this.txtpt.Margin = new System.Windows.Forms.Padding(6);
            this.txtpt.Name = "txtpt";
            this.txtpt.Size = new System.Drawing.Size(820, 35);
            this.txtpt.TabIndex = 4;
            this.txtpt.Text = "2084";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(78, 152);
            this.label2.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(130, 24);
            this.label2.TabIndex = 3;
            this.label2.Text = "解锁端口：";
            // 
            // txtus
            // 
            this.txtus.Location = new System.Drawing.Point(220, 234);
            this.txtus.Margin = new System.Windows.Forms.Padding(6);
            this.txtus.Name = "txtus";
            this.txtus.Size = new System.Drawing.Size(820, 35);
            this.txtus.TabIndex = 6;
            this.txtus.Text = "Administrator";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(78, 240);
            this.label3.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(106, 24);
            this.label3.TabIndex = 5;
            this.label3.Text = "用户名：";
            // 
            // txtpd
            // 
            this.txtpd.Location = new System.Drawing.Point(220, 322);
            this.txtpd.Margin = new System.Windows.Forms.Padding(6);
            this.txtpd.Name = "txtpd";
            this.txtpd.PasswordChar = '*';
            this.txtpd.Size = new System.Drawing.Size(820, 35);
            this.txtpd.TabIndex = 8;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(78, 328);
            this.label4.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(82, 24);
            this.label4.TabIndex = 7;
            this.label4.Text = "密码：";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(78, 416);
            this.label5.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(130, 24);
            this.label5.TabIndex = 9;
            this.label5.Text = "蓝牙设备：";
            // 
            // txtrssi
            // 
            this.txtrssi.Location = new System.Drawing.Point(220, 496);
            this.txtrssi.Margin = new System.Windows.Forms.Padding(6);
            this.txtrssi.Name = "txtrssi";
            this.txtrssi.Size = new System.Drawing.Size(820, 35);
            this.txtrssi.TabIndex = 13;
            this.txtrssi.Text = "-90";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(78, 504);
            this.label6.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(130, 24);
            this.label6.TabIndex = 12;
            this.label6.Text = "信号阈值：";
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(351, 774);
            this.button1.Margin = new System.Windows.Forms.Padding(6);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(176, 82);
            this.button1.TabIndex = 14;
            this.button1.Text = "检查更新";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // notifyIcon1
            // 
            this.notifyIcon1.ContextMenuStrip = this.contextMenuStrip1;
            this.notifyIcon1.Icon = ((System.Drawing.Icon)(resources.GetObject("notifyIcon1.Icon")));
            this.notifyIcon1.Text = "UnlockServer正在运行";
            this.notifyIcon1.Visible = true;
            this.notifyIcon1.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.notifyIcon1_MouseDoubleClick);
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.显示ToolStripMenuItem,
            this.隐藏ToolStripMenuItem,
            this.锁屏ToolStripMenuItem,
            this.退出ToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(137, 156);
            // 
            // 显示ToolStripMenuItem
            // 
            this.显示ToolStripMenuItem.Name = "显示ToolStripMenuItem";
            this.显示ToolStripMenuItem.Size = new System.Drawing.Size(136, 38);
            this.显示ToolStripMenuItem.Text = "显示";
            this.显示ToolStripMenuItem.Click += new System.EventHandler(this.显示ToolStripMenuItem_Click);
            // 
            // 隐藏ToolStripMenuItem
            // 
            this.隐藏ToolStripMenuItem.Name = "隐藏ToolStripMenuItem";
            this.隐藏ToolStripMenuItem.Size = new System.Drawing.Size(136, 38);
            this.隐藏ToolStripMenuItem.Text = "隐藏";
            this.隐藏ToolStripMenuItem.Click += new System.EventHandler(this.隐藏ToolStripMenuItem_Click);
            // 
            // 锁屏ToolStripMenuItem
            // 
            this.锁屏ToolStripMenuItem.Name = "锁屏ToolStripMenuItem";
            this.锁屏ToolStripMenuItem.Size = new System.Drawing.Size(136, 38);
            this.锁屏ToolStripMenuItem.Text = "锁屏";
            this.锁屏ToolStripMenuItem.Click += new System.EventHandler(this.锁屏ToolStripMenuItem_Click);
            // 
            // 退出ToolStripMenuItem
            // 
            this.退出ToolStripMenuItem.Name = "退出ToolStripMenuItem";
            this.退出ToolStripMenuItem.Size = new System.Drawing.Size(136, 38);
            this.退出ToolStripMenuItem.Text = "退出";
            this.退出ToolStripMenuItem.Click += new System.EventHandler(this.退出ToolStripMenuItem_Click);
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(78, 586);
            this.label7.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(130, 24);
            this.label7.TabIndex = 15;
            this.label7.Text = "设备类型：";
            // 
            // rdbclassic
            // 
            this.rdbclassic.AutoSize = true;
            this.rdbclassic.Enabled = false;
            this.rdbclassic.Location = new System.Drawing.Point(220, 586);
            this.rdbclassic.Margin = new System.Windows.Forms.Padding(6);
            this.rdbclassic.Name = "rdbclassic";
            this.rdbclassic.Size = new System.Drawing.Size(185, 28);
            this.rdbclassic.TabIndex = 16;
            this.rdbclassic.TabStop = true;
            this.rdbclassic.Text = "经典蓝牙设备";
            this.rdbclassic.UseVisualStyleBackColor = true;
            this.rdbclassic.CheckedChanged += new System.EventHandler(this.rdbclassic_CheckedChanged);
            // 
            // rdbble
            // 
            this.rdbble.AutoSize = true;
            this.rdbble.Enabled = false;
            this.rdbble.Location = new System.Drawing.Point(484, 582);
            this.rdbble.Margin = new System.Windows.Forms.Padding(6);
            this.rdbble.Name = "rdbble";
            this.rdbble.Size = new System.Drawing.Size(125, 28);
            this.rdbble.TabIndex = 17;
            this.rdbble.TabStop = true;
            this.rdbble.Text = "BLE设备";
            this.rdbble.UseVisualStyleBackColor = true;
            this.rdbble.CheckedChanged += new System.EventHandler(this.rdbble_CheckedChanged);
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(600, 774);
            this.button2.Margin = new System.Windows.Forms.Padding(6);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(176, 82);
            this.button2.TabIndex = 18;
            this.button2.Text = "取消配对";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // checkBox1
            // 
            this.checkBox1.AutoSize = true;
            this.checkBox1.Location = new System.Drawing.Point(900, 578);
            this.checkBox1.Margin = new System.Windows.Forms.Padding(6);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(138, 28);
            this.checkBox1.TabIndex = 20;
            this.checkBox1.Text = "开机自启";
            this.checkBox1.UseVisualStyleBackColor = true;
            this.checkBox1.Click += new System.EventHandler(this.checkBox1_Click);
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(78, 688);
            this.label8.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(130, 24);
            this.label8.TabIndex = 21;
            this.label8.Text = "功能选项：";
            // 
            // ckb_autolock
            // 
            this.ckb_autolock.AutoSize = true;
            this.ckb_autolock.Location = new System.Drawing.Point(220, 686);
            this.ckb_autolock.Margin = new System.Windows.Forms.Padding(6);
            this.ckb_autolock.Name = "ckb_autolock";
            this.ckb_autolock.Size = new System.Drawing.Size(138, 28);
            this.ckb_autolock.TabIndex = 22;
            this.ckb_autolock.Text = "自动锁定";
            this.ckb_autolock.UseVisualStyleBackColor = true;
            this.ckb_autolock.Click += new System.EventHandler(this.ckb_autolock_Click);
            // 
            // ckb_autounlock
            // 
            this.ckb_autounlock.AutoSize = true;
            this.ckb_autounlock.Location = new System.Drawing.Point(408, 686);
            this.ckb_autounlock.Margin = new System.Windows.Forms.Padding(6);
            this.ckb_autounlock.Name = "ckb_autounlock";
            this.ckb_autounlock.Size = new System.Drawing.Size(138, 28);
            this.ckb_autounlock.TabIndex = 23;
            this.ckb_autounlock.Text = "自动解锁";
            this.ckb_autounlock.UseVisualStyleBackColor = true;
            this.ckb_autounlock.Click += new System.EventHandler(this.ckb_autounlock_Click);
            // 
            // ckb_manuallock
            // 
            this.ckb_manuallock.AutoSize = true;
            this.ckb_manuallock.Location = new System.Drawing.Point(600, 686);
            this.ckb_manuallock.Margin = new System.Windows.Forms.Padding(6);
            this.ckb_manuallock.Name = "ckb_manuallock";
            this.ckb_manuallock.Size = new System.Drawing.Size(210, 28);
            this.ckb_manuallock.TabIndex = 24;
            this.ckb_manuallock.Text = "不干预人工锁定";
            this.toolTip1.SetToolTip(this.ckb_manuallock, "如果勾选此项，则人工锁定后，软件不会自动解锁");
            this.ckb_manuallock.UseVisualStyleBackColor = true;
            this.ckb_manuallock.Click += new System.EventHandler(this.ckb_manual_Click);
            // 
            // ckb_manuclunlock
            // 
            this.ckb_manuclunlock.AutoSize = true;
            this.ckb_manuclunlock.Location = new System.Drawing.Point(828, 686);
            this.ckb_manuclunlock.Margin = new System.Windows.Forms.Padding(6);
            this.ckb_manuclunlock.Name = "ckb_manuclunlock";
            this.ckb_manuclunlock.Size = new System.Drawing.Size(210, 28);
            this.ckb_manuclunlock.TabIndex = 25;
            this.ckb_manuclunlock.Text = "不干预人工解锁";
            this.toolTip1.SetToolTip(this.ckb_manuclunlock, "如果勾选此项，则人工解锁后，软件不会自动锁定");
            this.ckb_manuclunlock.UseVisualStyleBackColor = true;
            this.ckb_manuclunlock.Click += new System.EventHandler(this.ckb_manuclunlock_Click);
            // 
            // button3
            // 
            this.button3.Location = new System.Drawing.Point(862, 774);
            this.button3.Margin = new System.Windows.Forms.Padding(6);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(176, 82);
            this.button3.TabIndex = 26;
            this.button3.Text = "锁屏";
            this.toolTip1.SetToolTip(this.button3, "此按钮锁屏相当于软件锁屏，会自动解锁");
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // btn_searchDevice
            // 
            this.btn_searchDevice.Image = global::UnlockServer.Properties.Resources.dicsover;
            this.btn_searchDevice.Location = new System.Drawing.Point(971, 389);
            this.btn_searchDevice.Margin = new System.Windows.Forms.Padding(6);
            this.btn_searchDevice.Name = "btn_searchDevice";
            this.btn_searchDevice.Size = new System.Drawing.Size(78, 79);
            this.btn_searchDevice.TabIndex = 28;
            this.toolTip1.SetToolTip(this.btn_searchDevice, "搜索蓝牙设备");
            this.btn_searchDevice.UseVisualStyleBackColor = true;
            this.btn_searchDevice.Click += new System.EventHandler(this.btn_searchDevice_Click);
            // 
            // txt_bleDevice
            // 
            this.txt_bleDevice.Enabled = false;
            this.txt_bleDevice.Location = new System.Drawing.Point(220, 413);
            this.txt_bleDevice.Margin = new System.Windows.Forms.Padding(6);
            this.txt_bleDevice.Name = "txt_bleDevice";
            this.txt_bleDevice.Size = new System.Drawing.Size(739, 35);
            this.txt_bleDevice.TabIndex = 27;
            this.txt_bleDevice.Text = "00:00:00:00:00:00";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1088, 919);
            this.Controls.Add(this.btn_searchDevice);
            this.Controls.Add(this.txt_bleDevice);
            this.Controls.Add(this.button3);
            this.Controls.Add(this.ckb_manuclunlock);
            this.Controls.Add(this.ckb_manuallock);
            this.Controls.Add(this.ckb_autounlock);
            this.Controls.Add(this.ckb_autolock);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.checkBox1);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.rdbble);
            this.Controls.Add(this.rdbclassic);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.txtrssi);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.txtpd);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.txtus);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.txtpt);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.txtip);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btn_save);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(6);
            this.MaximizeBox = false;
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "蓝牙解锁工具_v1.2";
            this.contextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btn_save;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtip;
        private System.Windows.Forms.TextBox txtpt;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox txtus;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtpd;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox txtrssi;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.NotifyIcon notifyIcon1;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem 显示ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 退出ToolStripMenuItem;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.RadioButton rdbclassic;
        private System.Windows.Forms.RadioButton rdbble;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.CheckBox ckb_autolock;
        private System.Windows.Forms.CheckBox ckb_autounlock;
        private System.Windows.Forms.CheckBox ckb_manuallock;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.CheckBox ckb_manuclunlock;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.ToolStripMenuItem 锁屏ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 隐藏ToolStripMenuItem;
        private System.Windows.Forms.TextBox txt_bleDevice;
        private System.Windows.Forms.Button btn_searchDevice;
    }
}

