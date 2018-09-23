using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace olRW
{
    public class CommPort
    {
        static SerialPort Comm = new SerialPort();
        public bool Continuation = false;
        public bool Pause = false;

        public byte[] RxBuf;
        public int IdxRxBuf;

        private string Eos="";

        private Thread RxThread;

        public bool Open(string portname, String baudrate, string databit,
                         string stopbit, string parity, string handshake, string timeout, string bufsize)
        {
            if (portname == null || portname == "") return false;
            if (Comm.IsOpen) Close();

            Comm.PortName = portname;
            try
            {
                Comm.BaudRate = Convert.ToInt32(baudrate);
                Comm.DataBits = Convert.ToInt16(databit);
            }
            catch (FormatException ex) { return false; }

            if (handshake == "None")
            {
                Comm.RtsEnable = true;
                Comm.DtrEnable = true;
            }

            Comm.StopBits = (StopBits)Enum.Parse(typeof(StopBits), stopbit);
            Comm.Parity = (Parity)Enum.Parse(typeof(Parity), parity);
            Comm.Handshake = (Handshake)Enum.Parse(typeof(Handshake), handshake);
            Comm.WriteTimeout = Convert.ToInt16(timeout);

            try
            {
                IdxRxBuf = 0;
                RxBuf = new byte[Convert.ToInt16(bufsize)];
                Comm.Open();
                Continuation = true;
                RxThread = new Thread(ThreadReceive);
                RxThread.Start();
            }
            catch (System.Exception)
            {
                return false;
            }
            return true;
        }

        public void Close()
        {
            Continuation = false;
            RxThread?.Join();
        }

        public void SetEos(string eos)
        {
            Eos = eos;
        }

        public string GetEos()
        {
            return Eos;
        }

        public bool Send(Byte[] bytes)
        {
            if (!Comm.IsOpen) return false;
            try
            {
                Comm.Write(bytes, 0, bytes.Length);
            }
            catch (System.Exception)
            {
                throw new IOException("Write failed.");
            }
            return true;
        }

        private void ThreadReceive()
        {
            Continuation = true;
            Pause = false;

            while (Continuation)
            {
                System.Threading.Thread.Sleep(10);
                if (Pause || Comm.BytesToRead == 0) continue;

                int free = RxBuf.Length - IdxRxBuf;
                if (free > 0)
                {
                    try
                    {
                        int cnt = Comm.Read(RxBuf, IdxRxBuf, free);
                        IdxRxBuf += cnt;
                    }
                    catch (TimeoutException) { throw new IOException("Time out."); }
                }
                else
                {
                    throw new IOException("RxBuf is full.");
                }
            }
            Comm.Close();
        }
    }


    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        StringBuilder TxData = null;
        StringBuilder RxData = null;
        CommPort Comm = new CommPort();
        Task RxTask = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Page_LoadedAsync(object sender, RoutedEventArgs e)
        {
            TxData = new StringBuilder();
            RxData = new StringBuilder();
            RxTask = new Task(ReceiveTask);
            RxTask.Start();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Comm.Close();
            RxTask = null;
        }

        void ReceiveTask()
        {
            while ( RxTask != null )
            {
                if (Comm.IdxRxBuf > 0)
                {
                    Comm.Pause = true;

                    int rxlen = Comm.IdxRxBuf;
                    int freelen = Comm.RxBuf.Length - Comm.IdxRxBuf;
                    byte[] temp = new byte[rxlen];
                    Buffer.BlockCopy(Comm.RxBuf, 0, temp, 0, rxlen);
                    string text = System.Text.Encoding.ASCII.GetString(temp);
                    Comm.IdxRxBuf = 0;
                    Comm.Pause = false;
                    RxData.Append(text);

                    if (Comm.GetEos() != "")
                    {
                        if (-1 != text.IndexOf(Comm.GetEos())) Comm.SetEos("");
                    }
                }
            }
        }

        private void SerConnect_ClickAsync(object sender, RoutedEventArgs e)
        {
            Comm.Open(cmbPort.Text, cmbBaud.Text, "8", "1", "None", "None", "1000", "1024");
            txbStatus.Text = cmbPort.Text + ": port opend.";
        }

        private async void SerRead_ClickAsync(object sender, RoutedEventArgs e)
        {
            if( Comm.Continuation == false )
            {
                txbStatus.Text = "Comm port is not open ..";
                return;
            }

            for (Comm.SetEos(">"); Comm.GetEos() == ">";)
            {
                int ii = 0;
                Comm.Send(Encoding.ASCII.GetBytes("\r")) ;
                await Task.Delay(10);
                if( ++ii > 100 )
                {
                    txbStatus.Text = "Timeout ..";
                    return;
                }
            }

            RxData.Clear();
            Comm.Send(Encoding.ASCII.GetBytes("read " + txtFilename.Text + "\r\n"));
            for (Comm.SetEos(">"); Comm.GetEos() == ">";)
            {
                await Task.Delay(10);
                txbStatus.Text = string.Format("Length:{0}", RxData.Length);
            }

            string content = RxData.ToString();
            content = content.Replace("\r", "");
            content = content.Replace("\n", "");
            content = content.Replace(">", "");
            content = content.Replace("read " + txtFilename.Text, "");

            string filepath = txtFolderPath.Text + "\\" + txtFilename.Text;
            using (var fs = new FileStream(filepath, FileMode.Create, FileAccess.Write))
            {
                byte[] data = new byte[] { 0x00 };
                for (int ii = 0; ii < content.Length; ii+=2)
                {
                    try
                    {
                        data[0] = Convert.ToByte(content.Substring(ii, 2),16);
                        fs.Write(data, 0, data.Length);
                    }
                    catch(Exception ex)
                    {
                        txbStatus.Text = ex.ToString();
                        break;
                    }
                    txbStatus.Text = string.Format("Remain:{0}/{1}({2}%)", ii, content.Length, ii * 100 / content.Length);
                }
                fs.Close();
                txbStatus.Text = "read operation complete.";

            }
        }

        private async Task WaitPrompt()
        {
            RxData.Clear();
            for (Comm.SetEos(">"); Comm.GetEos() == ">";)
            {
                Comm.Send(Encoding.ASCII.GetBytes("\r"));
                await Task.Delay(10);
            }
        }

        private async void SerWrite_ClickAsync(object sender, RoutedEventArgs e)
        {
            byte[] buf;
            string filepath = txtFolderPath.Text + "\\" + txtFilename.Text;

            if (Comm.Continuation == false)
            {
                txbStatus.Text = "Comm port is not open ..";
                return;
            }

            try
            {
                using (var fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
                {
                    buf = new byte[fs.Length];
                    fs.Read(buf, 0, buf.Length);
                }
                txbStatus.Text = RxData.ToString();
            }
            catch (Exception ex)
            {
                txbStatus.Text = "not found file or folder.";
                return;
            }

            await WaitPrompt();

            Comm.Send(Encoding.ASCII.GetBytes("new " + txtFilename.Text + "\r\n"));
            txbStatus.Text = "new " + txtFilename.Text;

            await WaitPrompt();

            Comm.Send(Encoding.ASCII.GetBytes("append " + txtFilename.Text + "\r\n"));
            txbStatus.Text = "apppend" + txtFilename.Text;

            Comm.Send(Encoding.ASCII.GetBytes("\n\n"));

            StringBuilder sb = new StringBuilder();
            sb.Clear();
            for (int ii = 0; ii < buf.Length; ii++)
            {
                sb.Append(string.Format("{0:X2}", buf[ii]));
                if ((ii & 31) == 31)
                {
                    Comm.Send(Encoding.ASCII.GetBytes(sb.ToString()));
                    Comm.Send(Encoding.ASCII.GetBytes("\n"));
                    sb.Clear();
                    txbStatus.Text = string.Format("Remain:{0}/{1}({2}%)", ii, buf.Length, ii * 100 / buf.Length);
                    await Task.Delay(5);
                }
            }
            if( sb.Length != 0 )
            {
                Comm.Send(Encoding.ASCII.GetBytes(sb.ToString()));
                Comm.Send(Encoding.ASCII.GetBytes("\n"));
                sb.Clear();
            }

            txbStatus.Text = "write datas";

            await WaitPrompt();

            txbStatus.Text = "file write done";
        }

        private void txtFolderPath_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.Description = "please select folder";
            fbd.ShowNewFolderButton = true;
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtFolderPath.Text = fbd.SelectedPath;
            }
        }

        private void txtFilename_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            System.Windows.Forms.OpenFileDialog ofd = new System.Windows.Forms.OpenFileDialog();
            ofd.Title = "please select file";
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtFilename.Text = System.IO.Path.GetFileName( ofd.FileName );
            }
        }

        private void Grid_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop, true))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
            }
            else e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void Grid_Drop(object sender, System.Windows.DragEventArgs e)
        {
            var dropFiles = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string [];
            if (dropFiles == null) return;
            txtFolderPath.Text = System.IO.Path.GetDirectoryName(dropFiles[0]);
            txtFilename.Text = System.IO.Path.GetFileName(dropFiles[0]);
        }
    }
}
