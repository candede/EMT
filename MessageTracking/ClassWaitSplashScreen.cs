using System;
using System.Windows.Forms;
using System.Threading;
using System.Drawing;

namespace MessageTracking
{
    class ClassWaitSplashScreen
    {
        private object _lockObject = new object();
        private string _message = "Please Wait...";
        private Form _splashForm;
        private Point _centreForm;

        public ClassWaitSplashScreen(Form ParentForm)
        {
            if (ParentForm.Visible)
            {
                _centreForm.X = ParentForm.Left + (ParentForm.Width / 2);
                _centreForm.Y = ParentForm.Top + (ParentForm.Height / 2);
            }
            else
            {
                _centreForm.X = 0;
                _centreForm.Y = 0;
            }
        }

        public void Close()
        {
            lock (this._lockObject)
            {
                if (this.IsShowing)
                {
                    try
                    {
                        this._splashForm.Invoke(new MethodInvoker(this.CloseWindow));
                    }
                    catch (NullReferenceException)
                    {
                    }
                    this._splashForm = null;
                }
            }
        }

        private void CloseWindow()
        {
            this._splashForm.Dispose();
        }

        public void Show(string message)
        {
            if (this.IsShowing)
            {
                this.Close();
            }
            if (!string.IsNullOrEmpty(message))
            {
                this._message = message;
            }
            using (ManualResetEvent mrEvent = new ManualResetEvent(false))
            {
                Thread thread = new Thread(new ParameterizedThreadStart(this.ThreadStart));
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start(mrEvent);
                mrEvent.WaitOne();
            }
        }

        private void ThreadStart(object parameter)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            ManualResetEvent mrEvent = (ManualResetEvent)parameter;
            Application.EnableVisualStyles();
            _splashForm = new Form();
            _splashForm.Tag = mrEvent;
            _splashForm.ShowIcon = false;
            _splashForm.AutoSize = true;
            _splashForm.TopMost = true;
            _splashForm.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _splashForm.ControlBox = false;
            _splashForm.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            _splashForm.StartPosition = FormStartPosition.Manual;
            _splashForm.Cursor = Cursors.WaitCursor;
            _splashForm.FormClosing += new FormClosingEventHandler(this.WaitScreenClosing);
            _splashForm.Shown += new EventHandler(this.WaitScreenShown);


            Label label = new Label();
            label.Font = new Font(_splashForm.Font.FontFamily.Name, 11, FontStyle.Bold);
            label.Text = this._message;
            label.AutoSize = true;
            label.Padding = new Padding(20, 40, 20, 30);
            _splashForm.Controls.Add(label);

            if (_centreForm.X == 0 && _centreForm.Y == 0)
            {
                _splashForm.StartPosition = FormStartPosition.CenterScreen;
            }
            else
            {
                _splashForm.Left = _centreForm.X - (_splashForm.Width / 2);
                _splashForm.Top = _centreForm.Y - (_splashForm.Height / 2);
            }
            Application.Run(_splashForm);
            Application.ExitThread();
        }

        private void WaitScreenClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
            }
        }

        private void WaitScreenShown(object sender, EventArgs e)
        {
            Form form = (Form)sender;
            form.Shown -= new EventHandler(this.WaitScreenShown);
            ManualResetEvent tag = (ManualResetEvent)form.Tag;
            form.Tag = null;
            tag.Set();
        }

        public bool IsShowing
        {
            get
            {
                return (this._splashForm != null);
            }
        }
    }
}
