namespace UnlockServer
{
    partial class BtDeviceListForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(BtDeviceListForm));
            this.rdbble = new System.Windows.Forms.RadioButton();
            this.rdbclassic = new System.Windows.Forms.RadioButton();
            this.label7 = new System.Windows.Forms.Label();
            this.lstbldevice = new System.Windows.Forms.ListBox();
            this.button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // rdbble
            // 
            this.rdbble.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.rdbble.AutoSize = true;
            this.rdbble.Location = new System.Drawing.Point(499, 955);
            this.rdbble.Margin = new System.Windows.Forms.Padding(6);
            this.rdbble.Name = "rdbble";
            this.rdbble.Size = new System.Drawing.Size(125, 28);
            this.rdbble.TabIndex = 20;
            this.rdbble.TabStop = true;
            this.rdbble.Text = "BLE设备";
            this.rdbble.UseVisualStyleBackColor = true;
            this.rdbble.CheckedChanged += new System.EventHandler(this.rdbble_CheckedChanged);
            // 
            // rdbclassic
            // 
            this.rdbclassic.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.rdbclassic.AutoSize = true;
            this.rdbclassic.Location = new System.Drawing.Point(235, 959);
            this.rdbclassic.Margin = new System.Windows.Forms.Padding(6);
            this.rdbclassic.Name = "rdbclassic";
            this.rdbclassic.Size = new System.Drawing.Size(185, 28);
            this.rdbclassic.TabIndex = 19;
            this.rdbclassic.TabStop = true;
            this.rdbclassic.Text = "经典蓝牙设备";
            this.rdbclassic.UseVisualStyleBackColor = true;
            this.rdbclassic.CheckedChanged += new System.EventHandler(this.rdbclassic_CheckedChanged);
            // 
            // label7
            // 
            this.label7.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(93, 959);
            this.label7.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(130, 24);
            this.label7.TabIndex = 18;
            this.label7.Text = "设备类型：";
            // 
            // lstbldevice
            // 
            this.lstbldevice.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lstbldevice.FormattingEnabled = true;
            this.lstbldevice.ItemHeight = 24;
            this.lstbldevice.Location = new System.Drawing.Point(63, 16);
            this.lstbldevice.Name = "lstbldevice";
            this.lstbldevice.Size = new System.Drawing.Size(957, 892);
            this.lstbldevice.TabIndex = 21;
            this.lstbldevice.SelectedIndexChanged += new System.EventHandler(this.lstbldevice_SelectedIndexChanged);
            // 
            // button1
            // 
            this.button1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.button1.Location = new System.Drawing.Point(810, 939);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(203, 61);
            this.button1.TabIndex = 22;
            this.button1.Text = "确定";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // BtDeviceListForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1063, 1035);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.lstbldevice);
            this.Controls.Add(this.rdbble);
            this.Controls.Add(this.rdbclassic);
            this.Controls.Add(this.label7);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "BtDeviceListForm";
            this.Text = "蓝牙设备列表";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.RadioButton rdbble;
        private System.Windows.Forms.RadioButton rdbclassic;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.ListBox lstbldevice;
        private System.Windows.Forms.Button button1;
    }
}