using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;

namespace streetsmp
{
    class Form1 : Form
    {
        public const string TITLE = "Streets Racer Multiplayer Client 1.0 Alpha";
        public const string GAME_EXE = "Streets.exe";
        const string FILE_IP = "streetsmp.txt";
        const int PORT = 7777;
        //Client send
        const int BUFFER_SEND_SIZE = 0x20;
        const int THD_SLEEP = 40; //25 FPS
        //Client recieve
        const int BUFFER_SIZE = BUFFER_SEND_SIZE + 1;
        //Player, network car
        static uint[] PLAYER_ADDR = 
        {
            0x004AB300, //Checkpoint
            0x004A80C4, //Rotation
            0x004A8154, //Z
            0x004A81D4, //X,Y
        };
        static uint[] NET_ADDR =
        {
            0x004AB304, //Checkpoint
            0x004A9678, //Rotation
            0x004A96FC, //Z
            0x004A9770, //X,Y
        };
        static int[] DATA_LEN =
        {
            0x4,
            0xC,
            0x4,
            0xC
        };
        //Checkpoint calculation
        const uint PLAYER_CP_MAX = 0x004AB72C;
        const uint NET_CP_MAX = 0x004AB324;
        //AI
        uint[] AI_ADDR = { 0x0041622B, 0x00416243, 0x0041625D };
        byte[] ASM_NOP = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
        //Offset
        const uint CAR_OFFSET = 0xCE4;
        const uint CH_OFFSET = 0x4;
        //
        TextBox tb_ip;
        Button bt_connect;
        //
        StreamReader sr;
        StreamWriter sw;
        //
        TcpClient client;
        Socket sock;
        Thread thd, thd2;
        MemoryEdit.Memory mem;
        Process game;

        public Form1()
        {
            Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            Text = TITLE;
            ClientSize = new Size(320, 48);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            tb_ip = new TextBox();
            tb_ip.Bounds = new Rectangle(12, 12, 128, 24);
            tb_ip.MaxLength = 15;
            if (File.Exists(FILE_IP))
            {
                sr = new StreamReader(FILE_IP);
                tb_ip.Text = sr.ReadLine();
                sr.Close();
            }
            Controls.Add(tb_ip);
            bt_connect = new Button();
            bt_connect.Text = "Connect";
            bt_connect.Bounds = new Rectangle(ClientRectangle.Right - 140, 12, 128, 24);
            bt_connect.Click += bt_connect_Click;
            Controls.Add(bt_connect);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (game != null && !game.HasExited)
                game.Kill();
            Environment.Exit(0);
            base.OnClosed(e);
        }

        void bt_connect_Click(object sender, EventArgs e)
        {
            bt_connect.Enabled = false;
            tb_ip.Enabled = false;
            try
            {
                client = new TcpClient(tb_ip.Text, PORT);
                sw = new StreamWriter(FILE_IP, false, Encoding.Default);
                sw.Write(tb_ip.Text);
                sw.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Source + " - " + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                bt_connect.Enabled = true;
                tb_ip.Enabled = true;
                return;
            }
            game = Process.Start(GAME_EXE);
            mem = new MemoryEdit.Memory(game, 0x001F0FFF);
            //Disable AI
            mem.WriteByte(AI_ADDR[0], ASM_NOP, ASM_NOP.Length - 1);
            mem.WriteByte(AI_ADDR[1], ASM_NOP, ASM_NOP.Length);
            mem.WriteByte(AI_ADDR[2], ASM_NOP, ASM_NOP.Length);
            //
            sock = client.Client;
            thd = new Thread(new ThreadStart(NetRec));
            thd.Start();
            thd2 = new Thread(new ThreadStart(NetSend));
            thd2.Start();
        }

        byte[] CalculateCheckpoint(int check)
        {
            int player_max = BitConverter.ToInt32(mem.ReadByte(PLAYER_CP_MAX, 4), 0);
            int net_max = BitConverter.ToInt32(mem.ReadByte(NET_CP_MAX, 4), 0) - 1;
            float diff = (float)check / player_max;
            int ret = (int)(diff * net_max);
            return BitConverter.GetBytes(ret);
        }

        void NetRec()
        {
            try
            {
                byte[] buffer = new byte[BUFFER_SIZE];
                byte[] data = new byte[BUFFER_SEND_SIZE];
                byte[] ch;
                byte id;
                uint tmp;
                int src_idx;
                int len;
                while (true)
                {
                    for (int idx = 0; idx < BUFFER_SIZE; idx += sock.Receive(buffer, idx, BUFFER_SIZE - idx, SocketFlags.None)) ;
                    //Player id
                    id = (byte)(buffer[0] % 3);
                    //Checkpoint
                    len = DATA_LEN[0];
                    ch = CalculateCheckpoint(BitConverter.ToInt32(buffer, 1));
                    tmp = NET_ADDR[0] + CH_OFFSET * id;
                    mem.WriteByte(tmp, ch, len);
                    //Rotation, Movement
                    src_idx = 1;
                    for (int i = 1; i < NET_ADDR.Length; i++)
                    {
                        src_idx += len;
                        len = DATA_LEN[i];
                        Array.Copy(buffer, src_idx, data, 0, len);
                        tmp = NET_ADDR[i] + CAR_OFFSET * id;
                        mem.WriteByte(tmp, data, len);
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Source + " - " + e.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (game != null && !game.HasExited)
                    game.Kill();
                Environment.Exit(0);
            }
        }

        void NetSend()
        {
            try
            {
                byte[] buffer = new byte[BUFFER_SEND_SIZE];
                byte[] data;
                int trg_idx;
                int len;
                while (true)
                {
                    Thread.Sleep(THD_SLEEP);
                    trg_idx = 0;
                    for (int i = 0; i < PLAYER_ADDR.Length; i++)
                    {
                        len = DATA_LEN[i];
                        data = mem.ReadByte(PLAYER_ADDR[i], len);
                        Array.Copy(data, 0, buffer, trg_idx, len);
                        trg_idx += len;
                    }
                    sock.Send(buffer);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Source + " - " + e.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (game != null && !game.HasExited)
                    game.Kill();
                Environment.Exit(0);
            }
        }
    }

    class Progam
    {
        [STAThread]
        static void Main()
        {
            if (!File.Exists(Form1.GAME_EXE))
            {
                MessageBox.Show("Game not found! (" + Form1.GAME_EXE + ")",
                    Form1.TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}