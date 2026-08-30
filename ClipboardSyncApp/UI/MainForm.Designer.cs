namespace ClipboardSyncApp.UI;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private System.Windows.Forms.Label peerIpLabel;
    private System.Windows.Forms.TextBox peerIpTextBox;
    private System.Windows.Forms.Label portLabel;
    private System.Windows.Forms.NumericUpDown portNumericUpDown;
    private System.Windows.Forms.Button connectButton;
    private System.Windows.Forms.Button sendTestButton;
    private System.Windows.Forms.TextBox statusTextBox;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.peerIpLabel = new System.Windows.Forms.Label();
        this.peerIpTextBox = new System.Windows.Forms.TextBox();
        this.portLabel = new System.Windows.Forms.Label();
        this.portNumericUpDown = new System.Windows.Forms.NumericUpDown();
        this.connectButton = new System.Windows.Forms.Button();
        this.sendTestButton = new System.Windows.Forms.Button();
        this.statusTextBox = new System.Windows.Forms.TextBox();

        ((System.ComponentModel.ISupportInitialize)(this.portNumericUpDown)).BeginInit();
        this.SuspendLayout();

        this.peerIpLabel.AutoSize = true;
        this.peerIpLabel.Location = new System.Drawing.Point(20, 24);
        this.peerIpLabel.Name = "peerIpLabel";
        this.peerIpLabel.Size = new System.Drawing.Size(98, 15);
        this.peerIpLabel.TabIndex = 0;
        this.peerIpLabel.Text = "Peer Tailscale IP";

        this.peerIpTextBox.Location = new System.Drawing.Point(140, 20);
        this.peerIpTextBox.Name = "peerIpTextBox";
        this.peerIpTextBox.Size = new System.Drawing.Size(180, 23);
        this.peerIpTextBox.TabIndex = 1;

        this.portLabel.AutoSize = true;
        this.portLabel.Location = new System.Drawing.Point(20, 60);
        this.portLabel.Name = "portLabel";
        this.portLabel.Size = new System.Drawing.Size(32, 15);
        this.portLabel.TabIndex = 2;
        this.portLabel.Text = "Port";

        this.portNumericUpDown.Location = new System.Drawing.Point(140, 56);
        this.portNumericUpDown.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
        this.portNumericUpDown.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        this.portNumericUpDown.Name = "portNumericUpDown";
        this.portNumericUpDown.Size = new System.Drawing.Size(120, 23);
        this.portNumericUpDown.TabIndex = 3;
        this.portNumericUpDown.Value = new decimal(new int[] { 5001, 0, 0, 0 });

        this.connectButton.Location = new System.Drawing.Point(280, 52);
        this.connectButton.Name = "connectButton";
        this.connectButton.Size = new System.Drawing.Size(120, 30);
        this.connectButton.TabIndex = 4;
        this.connectButton.Text = "Connect";
        this.connectButton.UseVisualStyleBackColor = true;
        this.connectButton.Click += new System.EventHandler(this.connectButton_Click);

        this.sendTestButton.Location = new System.Drawing.Point(420, 52);
        this.sendTestButton.Name = "sendTestButton";
        this.sendTestButton.Size = new System.Drawing.Size(120, 30);
        this.sendTestButton.TabIndex = 5;
        this.sendTestButton.Text = "Send Test";
        this.sendTestButton.UseVisualStyleBackColor = true;
        this.sendTestButton.Click += new System.EventHandler(this.sendTestButton_Click);

        this.statusTextBox.Location = new System.Drawing.Point(20, 100);
        this.statusTextBox.Multiline = true;
        this.statusTextBox.Name = "statusTextBox";
        this.statusTextBox.ReadOnly = true;
        this.statusTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
        this.statusTextBox.Size = new System.Drawing.Size(520, 200);
        this.statusTextBox.TabIndex = 6;

        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(560, 320);
        this.Controls.Add(this.statusTextBox);
        this.Controls.Add(this.sendTestButton);
        this.Controls.Add(this.connectButton);
        this.Controls.Add(this.portNumericUpDown);
        this.Controls.Add(this.portLabel);
        this.Controls.Add(this.peerIpTextBox);
        this.Controls.Add(this.peerIpLabel);
        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Name = "MainForm";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.Text = "Clipboard Sync";

        ((System.ComponentModel.ISupportInitialize)(this.portNumericUpDown)).EndInit();
        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
