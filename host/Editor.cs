using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace DesktopPanel {
    internal sealed class Editor : Form {
        private sealed class Choice { internal string Value, Label; public override string ToString() { return Label; } }
        private readonly Dictionary<string, Control> inputs = new Dictionary<string, Control>();
        private readonly Dictionary<string, string> kinds = new Dictionary<string, string>();
        private readonly Dictionary<string, Dictionary<string, object>> presets = new Dictionary<string, Dictionary<string, object>>();
        internal event Action<Dictionary<string, object>> PreviewChanged;
        internal Editor(string title, IList fields, float scale) {
            Text = title; Font = new Font("Microsoft YaHei UI", 10F); AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = false;
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding((int)(22 * scale)), ColumnCount = 2, AutoScroll = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110 * scale)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int index = 0;
            foreach (object item in fields) {
                var field = (Dictionary<string, object>)item;
                string name = Convert.ToString(field["name"]), type = Convert.ToString(field["type"]), value = field.ContainsKey("value") ? Convert.ToString(field["value"], CultureInfo.InvariantCulture) : "";
                var label = new Label { Text = Convert.ToString(field["label"]), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, (int)(10 * scale), (int)(10 * scale), (int)(10 * scale)) };
                Control input;
                if (type == "date" || type == "time") {
                    var date = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = type == "date" ? "yyyy-MM-dd" : "HH:mm", ShowUpDown = type == "time", Width = 260 };
                    DateTime parsed; if (DateTime.TryParse(value, out parsed)) date.Value = parsed;
                    input = date;
                } else if (type == "range") {
                    var slider = new TrackBar { Minimum = Convert.ToInt32(field["min"]), Maximum = Convert.ToInt32(field["max"]), TickStyle = TickStyle.None, AutoSize = false, Height = (int)(30 * scale), SmallChange = 1, LargeChange = 5 };
                    if (field.ContainsKey("step")) slider.SmallChange = slider.LargeChange = Convert.ToInt32(field["step"]);
                    int parsed; if (Int32.TryParse(value, out parsed)) slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, parsed));
                    input = slider;
                } else if (type == "number") {
                    var number = new NumericUpDown { Minimum = field.ContainsKey("min") ? Convert.ToDecimal(field["min"]) : 0, Maximum = field.ContainsKey("max") ? Convert.ToDecimal(field["max"]) : 100, Width = 260 };
                    decimal parsed; if (Decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)) number.Value = Math.Max(number.Minimum, Math.Min(number.Maximum, parsed));
                    input = number;
                } else if (type == "select") {
                    var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
                    foreach (object option in (IList)field["options"]) { var choice = option as Dictionary<string,object>; combo.Items.Add(choice == null ? (object)Convert.ToString(option) : new Choice { Value = Convert.ToString(choice["value"]), Label = Convert.ToString(choice["label"]) }); }
                    for (int at=0; at<combo.Items.Count; at++) { var choice=combo.Items[at] as Choice; if ((choice != null ? choice.Value : Convert.ToString(combo.Items[at])) == value) combo.SelectedIndex=at; } if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
                    input = combo;
                } else { input = new TextBox { Text = value, MaxLength = 1000, Width = 260 }; }
                if (field.ContainsKey("presets")) presets[name] = (Dictionary<string, object>)field["presets"];
                input.Width = (int)(260 * scale);
                input.Anchor = AnchorStyles.Left | AnchorStyles.Right; input.Margin = new Padding(0, (int)(8 * scale), 0, (int)(8 * scale));
                inputs.Add(name, input); kinds.Add(name, type);
                table.Controls.Add(label, 0, index);
                if (type == "color") {
                    var holder = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
                    holder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); holder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48 * scale));
                    var pick = new Button { Text = "●", Dock = DockStyle.Fill, Margin = new Padding((int)(5*scale),(int)(8*scale),0,(int)(8*scale)) };
                    Control colorInput = input;
                    pick.Click += delegate { using (var color = new ColorDialog { FullOpen = true }) { try { color.Color = ColorTranslator.FromHtml(colorInput.Text); } catch { } if (color.ShowDialog(this) == DialogResult.OK) colorInput.Text = "#" + color.Color.R.ToString("x2") + color.Color.G.ToString("x2") + color.Color.B.ToString("x2"); } };
                    holder.Controls.Add(input,0,0); holder.Controls.Add(pick,1,0); table.Controls.Add(holder,1,index++);
                } else if (type == "range") {
                    var holder = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
                    holder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); holder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45 * scale));
                    var slider = (TrackBar)input;var output = new Label { Text = slider.Value.ToString(), AutoSize = true, Anchor = AnchorStyles.Left };
                    slider.ValueChanged += delegate { output.Text = slider.Value.ToString(); };
                    holder.Controls.Add(slider,0,0);holder.Controls.Add(output,1,0);table.Controls.Add(holder,1,index++);
                } else table.Controls.Add(input, 1, index++);
            }
            foreach (var item in inputs) {
                var slider = item.Value as TrackBar;var number = item.Value as NumericUpDown;var combo = item.Value as ComboBox;var date = item.Value as DateTimePicker;
                if (slider != null) slider.ValueChanged += delegate { NotifyPreview(); };
                else if (number != null) number.ValueChanged += delegate { NotifyPreview(); };
                else if (combo != null) combo.SelectedIndexChanged += delegate { NotifyPreview(); };
                else if (date != null) date.ValueChanged += delegate { NotifyPreview(); };
                else item.Value.TextChanged += delegate { NotifyPreview(); };
            }
            foreach (var preset in presets) {
                string name = preset.Key; var combo = inputs[name] as ComboBox;
                if (combo != null) combo.SelectedIndexChanged += delegate { object selected; var choice=combo.SelectedItem as Choice; if (presets[name].TryGetValue(choice != null ? choice.Value : combo.Text, out selected)) foreach (var value in (Dictionary<string, object>)selected) if (inputs.ContainsKey(value.Key)) { var date=inputs[value.Key] as DateTimePicker; DateTime parsed; if (date != null && DateTime.TryParse(Convert.ToString(value.Value), out parsed)) date.Value=parsed; else inputs[value.Key].Text=Convert.ToString(value.Value); } };
            }
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, (int)(18 * scale), 0, 0) };
            var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size((int)(75 * scale), (int)(30 * scale)) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size((int)(75 * scale), (int)(30 * scale)) };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            table.Controls.Add(buttons, 0, index); table.SetColumnSpan(buttons, 2);
            AcceptButton = ok; CancelButton = cancel; Controls.Add(table);
            ClientSize = new Size((int)(470 * scale), Math.Min(Screen.FromPoint(Cursor.Position).WorkingArea.Height - 120, (int)((110 + fields.Count * 48) * scale)));
        }
        private void NotifyPreview() { if (PreviewChanged != null) PreviewChanged(Result()); }
        internal Dictionary<string, object> Result() {
            var result = new Dictionary<string, object>();
            foreach (var item in inputs) {
                var date = item.Value as DateTimePicker; var number = item.Value as NumericUpDown;var slider = item.Value as TrackBar;
                var combo=item.Value as ComboBox; var choice=combo != null ? combo.SelectedItem as Choice : null;
                result[item.Key] = slider != null ? (object)slider.Value : date != null ? (object)date.Value.ToString(kinds[item.Key] == "date" ? "yyyy-MM-dd" : "HH:mm", CultureInfo.InvariantCulture) : number != null ? (object)number.Value : choice != null ? (object)choice.Value : item.Value.Text;
            }
            return result;
        }
    }
}
