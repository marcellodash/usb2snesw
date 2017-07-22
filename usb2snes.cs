﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Management;
using System.Collections;
using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using usb2snes.utils;
using usb2snes.Properties;

namespace WindowsFormsApplication1
{
    public partial class usb2snes : Form
    {
        enum usbint_server_opcode_e
        {
            // address space operations
            USBINT_SERVER_OPCODE_GET = 0,
            USBINT_SERVER_OPCODE_PUT,
            USBINT_SERVER_OPCODE_EXECUTE,
            USBINT_SERVER_OPCODE_ATOMIC,

            // file system operations
            USBINT_SERVER_OPCODE_LS,
            USBINT_SERVER_OPCODE_MKDIR,
            USBINT_SERVER_OPCODE_RM,
            USBINT_SERVER_OPCODE_MV,

            // special operations
            USBINT_SERVER_OPCODE_RESET,
            USBINT_SERVER_OPCODE_BOOT,
            USBINT_SERVER_OPCODE_MENU_LOCK,
            USBINT_SERVER_OPCODE_MENU_UNLOCK,
            USBINT_SERVER_OPCODE_MENU_RESET,
            USBINT_SERVER_OPCODE_EXE,
            USBINT_SERVER_OPCODE_TIME,

            // response
            USBINT_SERVER_OPCODE_RESPONSE,
        };

        enum usbint_server_space_e
        {
            USBINT_SERVER_SPACE_FILE = 0,
            USBINT_SERVER_SPACE_SNES,
        };

        enum usbint_server_flags_e
        {
            USBINT_SERVER_FLAGS_NONE = 0,
            USBINT_SERVER_FLAGS_FAST = 1,
            USBINT_SERVER_FLAGS_CLRX = 2,
            USBINT_SERVER_FLAGS_SETX = 4,
        };

        public usb2snes()
        {
            InitializeComponent();
            //PopulateTreeViewLocal();
            listViewRemote.ListViewItemSorter = new Sorter();
            listViewLocal.ListViewItemSorter = new Sorter();
        }

        // item
        private class Item
        {
            public string PortName;
            public string PortDesc;

            public Item(string name, string desc)
            {
                PortName = name;
                PortDesc = desc;
            }

            public override string ToString()
            {
                return PortDesc;
            }
        }

        private void buttonRefresh_Click(object sender, EventArgs e)
        {
            comboBoxPort.Items.Clear();
            comboBoxPort.ResetText();
            comboBoxPort.SelectedIndex = -1;

            EnableButtons(false);

            var deviceList = Win32DeviceMgmt.GetAllCOMPorts();
            bool found = false;
            foreach (var device in deviceList)
            {
                if (device.bus_description.Contains("sd2snes"))
                {
                    Item item = new Item(device.name.Trim(), device.name.Trim() + " - " + device.bus_description.Trim() + " - " + device.description.Trim());
                    int index = comboBoxPort.Items.Add(item);
                    if (!found)
                    {
                        found = true;
                        comboBoxPort.SelectedIndex = index;
                    }
                }
            }
        }

        private void comboBoxPort_SelectedIndexChanged(object sender, EventArgs e)
        {
            EnableButtons(false);
            connected = false;

            if (comboBoxPort.SelectedIndex >= 0)
            {
                Item item = (Item)comboBoxPort.SelectedItem;

                if (item.PortDesc.Contains("sd2snes"))
                {
                    remoteDirPrev = "";
                    remoteDir = "";
                    remoteDirNext = "";
                    RefreshListViewRemote();
                }
            }
        }

        private void buttonUpload_Click(object sender, EventArgs e)
        {
            try
            {
                if (connected)
                {
                    //openFileDialog1.Title = "ROM file to load";
                    //openFileDialog1.Filter = "ROM File|*.sfc;*.smc"
                    //                       + "|SRAM File|*.srm"
                    //                       + "|Cheat File|*.yml"
                    //                       + "|All Supported Types|*.sfc;*.smc;*.srm;*.yml"
                    //                       + "|All Files|*.*";
                    //openFileDialog1.FileName = "";

                    //if (openFileDialog1.ShowDialog() != DialogResult.Cancel)
                    //{
                    if (listViewLocal.SelectedItems.Count > 0) {
                        EnableButtons(false);

                        ConnectUSB();

                        //for (int i = 0; i < openFileDialog1.FileNames.Length; i++)
                        foreach (ListViewItem item in listViewLocal.SelectedItems)
                        {
                            if (item.ImageIndex == 0) continue;

                            string fileName = localDir + @"\" + item.Text; //openFileDialog1.FileNames[i];
                            string safeFileName = item.Text; //openFileDialog1.SafeFileNames[i];

                            //{
                            FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);

                            byte[] tBuffer = new byte[512];
                            int curSize = 0;

                            // send write command
                            Array.Clear(tBuffer, 0, tBuffer.Length);
                            tBuffer[0] = Convert.ToByte('U');
                            tBuffer[1] = Convert.ToByte('S');
                            tBuffer[2] = Convert.ToByte('B');
                            tBuffer[3] = Convert.ToByte('A');
                            tBuffer[4] = Convert.ToByte(usbint_server_opcode_e.USBINT_SERVER_OPCODE_PUT); // opcode
                            tBuffer[5] = Convert.ToByte(usbint_server_space_e.USBINT_SERVER_SPACE_FILE); // space
                            tBuffer[6] = Convert.ToByte(usbint_server_flags_e.USBINT_SERVER_FLAGS_NONE); // flags

                            long fileSize = new FileInfo(fileName).Length;
                            tBuffer[252] = Convert.ToByte((fileSize >> 24) & 0xFF);
                            tBuffer[253] = Convert.ToByte((fileSize >> 16) & 0xFF);
                            tBuffer[254] = Convert.ToByte((fileSize >> 8) & 0xFF);
                            tBuffer[255] = Convert.ToByte((fileSize >> 0) & 0xFF);

                            // leave a trailing 0 to terminate the string if it is too long
                            Buffer.BlockCopy(ASCIIEncoding.ASCII.GetBytes(remoteDir + "/" + safeFileName), 0, tBuffer, 256, Math.Min(255, ASCIIEncoding.ASCII.GetBytes(remoteDir + "/" + safeFileName).Length));

                            serialPort1.Write(tBuffer, 0, tBuffer.Length);

                            // read info
                            Array.Clear(tBuffer, 0, tBuffer.Length);
                            curSize = 0;
                            while (curSize < 512) curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));

                            // write data
                            Array.Clear(tBuffer, 0, tBuffer.Length);
                            curSize = 0;
                            toolStripProgressBar1.Value = 0;
                            toolStripProgressBar1.Enabled = true;
                            toolStripStatusLabel1.Text = "uploading: " + safeFileName;
                            while (curSize < fs.Length)
                            {
                                int bytesToWrite = fs.Read(tBuffer, 0, 512);
                                serialPort1.Write(tBuffer, 0, bytesToWrite);
                                curSize += bytesToWrite;
                                toolStripProgressBar1.Value = 100 * curSize / (int)fs.Length;
                            }
                            toolStripStatusLabel1.Text = "idle";
                            toolStripProgressBar1.Enabled = false;

                            fs.Close();
                        }

                        System.Threading.Thread.Sleep(100);

                        serialPort1.Close();

                        RefreshListViewRemote();
                    }
                }
            }
            catch (Exception x)
            {
                toolStripStatusLabel1.Text = x.Message.ToString();
                try { serialPort1.Close(); } catch (Exception x1) { }
                connected = false;
            }
        }

        private void buttonDownload_Click(object sender, EventArgs e)
        {
            try
            {
                if (connected)
                {
                    EnableButtons(false);

                    ConnectUSB();

                    foreach (ListViewItem item in listViewRemote.SelectedItems)
                    {
                        if (item.ImageIndex == 1)
                        {
                            string name = remoteDir + '/' + item.Text;
                            if (name.Length < 256)
                            {
                                //saveFileDialog1.Title = "ROM file to Save";
                                //saveFileDialog1.Filter = "All Files|*.*|ROM File|*.sfc;*.smc";
                                //saveFileDialog1.FileName = item.Text;
                                //if (saveFileDialog1.ShowDialog() != DialogResult.Cancel)
                                //{
                                FileStream fs = new FileStream(localDir + @"\" + item.Text, FileMode.Create, FileAccess.Write);
                                //BinaryWriter bs = new BinaryWriter(fs);

                                byte[] tBuffer = new byte[512];
                                int curSize = 0;

                                // send read command
                                Array.Clear(tBuffer, 0, tBuffer.Length);
                                tBuffer[0] = Convert.ToByte('U');
                                tBuffer[1] = Convert.ToByte('S');
                                tBuffer[2] = Convert.ToByte('B');
                                tBuffer[3] = Convert.ToByte('A');
                                tBuffer[4] = Convert.ToByte(usbint_server_opcode_e.USBINT_SERVER_OPCODE_GET); // opcode
                                tBuffer[5] = Convert.ToByte(usbint_server_space_e.USBINT_SERVER_SPACE_FILE); // space
                                tBuffer[6] = Convert.ToByte(usbint_server_flags_e.USBINT_SERVER_FLAGS_NONE); // flags

                                // directory
                                Buffer.BlockCopy(ASCIIEncoding.ASCII.GetBytes(name.ToArray()), 0, tBuffer, 256, name.Length);

                                serialPort1.Write(tBuffer, 0, tBuffer.Length);

                                // read response
                                Array.Clear(tBuffer, 0, tBuffer.Length);
                                curSize = 0;
                                while (curSize < 512) curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));
                                int fileSize = 0;
                                fileSize |= tBuffer[252]; fileSize <<= 8;
                                fileSize |= tBuffer[253]; fileSize <<= 8;
                                fileSize |= tBuffer[254]; fileSize <<= 8;
                                fileSize |= tBuffer[255]; fileSize <<= 0;

                                // read data
                                Array.Clear(tBuffer, 0, tBuffer.Length);
                                curSize = 0;
                                toolStripProgressBar1.Value = 0;
                                toolStripProgressBar1.Enabled = true;
                                toolStripStatusLabel1.Text = "downloading: " + name;
                                while (curSize < fileSize)
                                {
                                    int prevSize = curSize;
                                    curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));
                                    fs.Write(tBuffer, (prevSize % 512), curSize - prevSize);
                                    toolStripProgressBar1.Value = 100 * curSize / fileSize;
                                }
                                toolStripStatusLabel1.Text = "idle";
                                toolStripProgressBar1.Enabled = false;

                                fs.Close();
                                //}
                            }
                        }
                    }

                    System.Threading.Thread.Sleep(100);

                    serialPort1.Close();

                    RefreshListViewRemote();
                    RefreshListViewLocal();
                }
            }
            catch (Exception x)
            {
                toolStripStatusLabel1.Text = x.Message.ToString();
                try { serialPort1.Close(); } catch (Exception x1) { }
                connected = false;
            }
        }

        private void buttonBoot_Click(object sender, EventArgs e)
        {
            try
            {
                if (connected)
                {
                    EnableButtons(false);

                    ConnectUSB();

                    foreach (ListViewItem item in listViewRemote.SelectedItems)
                    {
                        var ext = Path.GetExtension(item.Text);
                        if (item.ImageIndex == 1 && (ext.Contains("sfc") | ext.Contains("smc") | ext.Contains("fig")))
                        {
                            string name = remoteDir + '/' + item.Text;
                            if (name.Length < 256)
                            {

                                byte[] tBuffer = new byte[512];

                                // send boot command
                                Array.Clear(tBuffer, 0, tBuffer.Length);
                                tBuffer[0] = Convert.ToByte('U');
                                tBuffer[1] = Convert.ToByte('S');
                                tBuffer[2] = Convert.ToByte('B');
                                tBuffer[3] = Convert.ToByte('A'); // directory listing
                                tBuffer[4] = Convert.ToByte(usbint_server_opcode_e.USBINT_SERVER_OPCODE_BOOT); // opcode
                                tBuffer[5] = Convert.ToByte(usbint_server_space_e.USBINT_SERVER_SPACE_FILE); // space
                                tBuffer[6] = Convert.ToByte(usbint_server_flags_e.USBINT_SERVER_FLAGS_NONE); // flags

                                // directory
                                Buffer.BlockCopy(ASCIIEncoding.ASCII.GetBytes(name.ToArray()), 0, tBuffer, 256, name.Length);

                                serialPort1.Write(tBuffer, 0, tBuffer.Length);

                                System.Threading.Thread.Sleep(100); // for read?

                                // read response
                                Array.Clear(tBuffer, 0, tBuffer.Length);
                                int curSize = 0;
                                while (curSize < 512) curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));

                                break; // only boot the first file
                            }
                        }
                    }

                    EnableButtons(true);

                    System.Threading.Thread.Sleep(100); // for close

                    serialPort1.Close();
                }
            }
            catch (Exception x)
            {
                toolStripStatusLabel1.Text = x.Message.ToString();
                try { serialPort1.Close(); } catch (Exception x1) { }
                connected = false;
            }
        }

        //private void PopulateTreeViewLocal()
        //{
        //    TreeNode rootNode;

        //    DirectoryInfo info = new DirectoryInfo(@"C:\Users\orion\Downloads\goodroms");

        //    if (info.Exists)
        //    {
        //        rootNode = new TreeNode(info.Name);
        //        rootNode.Tag = info;
        //        GetDirectories(info.GetDirectories(), rootNode);
        //        treeViewLocal.Nodes.Add(rootNode);
        //    }
        //}

        private void GetDirectories(DirectoryInfo[] subDirs, TreeNode root)
        {

            foreach (DirectoryInfo subDir in subDirs)
            {
                TreeNode node = new TreeNode(subDir.Name, 0, 0);
                node.Tag = subDir;
                node.ImageKey = "folder";
                DirectoryInfo[] subSubDirs = subDir.GetDirectories();

                if (subSubDirs.Length != 0) GetDirectories(subSubDirs, node);

                root.Nodes.Add(node);
            }
        }

        //private void treeViewLocal_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        //{
        //    TreeNode selectedNode = e.Node;
        //    listViewLocal.Clear();
        //    DirectoryInfo nodeDirInfo = (DirectoryInfo)selectedNode.Tag;

        //    foreach (DirectoryInfo dir in nodeDirInfo.GetDirectories())
        //    {
        //        ListViewItem item = new ListViewItem(dir.Name, 0);
        //        ListViewItem.ListViewSubItem[] subItems = new ListViewItem.ListViewSubItem[] { new ListViewItem.ListViewSubItem(item, "Directory"), new ListViewItem.ListViewSubItem(item, dir.LastAccessTime.ToShortDateString()) };
        //        item.SubItems.AddRange(subItems);
        //        listViewLocal.Items.Add(item);
        //    }

        //    foreach (FileInfo file in nodeDirInfo.GetFiles())
        //    {
        //        ListViewItem item = new ListViewItem(file.Name, 1);
        //        ListViewItem.ListViewSubItem[] subItems = new ListViewItem.ListViewSubItem[] { new ListViewItem.ListViewSubItem(item, "File"), new ListViewItem.ListViewSubItem(item, file.LastAccessTime.ToShortDateString()) };
        //        item.SubItems.AddRange(subItems);
        //        listViewLocal.Items.Add(item);
        //    }

        //    //listViewLocal.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
        //}

        private void RefreshListViewRemote()
        {
            connected = false;

            // connect to the snes
            try
            {
                if (!serialPort1.IsOpen)
                {
                    listViewRemote.Clear();

                    EnableButtons(false);

                    ConnectUSB();

                    int curSize = 0;
                    byte[] tBuffer = new byte[512];

                    // send directory command
                    Array.Clear(tBuffer, 0, tBuffer.Length);
                    tBuffer[0] = Convert.ToByte('U');
                    tBuffer[1] = Convert.ToByte('S');
                    tBuffer[2] = Convert.ToByte('B');
                    tBuffer[3] = Convert.ToByte('A'); // directory listing
                    tBuffer[4] = Convert.ToByte(usbint_server_opcode_e.USBINT_SERVER_OPCODE_LS); // opcode
                    tBuffer[5] = Convert.ToByte(usbint_server_space_e.USBINT_SERVER_SPACE_FILE); // space
                    tBuffer[6] = Convert.ToByte(usbint_server_flags_e.USBINT_SERVER_FLAGS_NONE); // flags

                    // leave a trailing 0 to terminate the string if it is too long
                    Buffer.BlockCopy(ASCIIEncoding.ASCII.GetBytes(remoteDir.ToArray()), 0, tBuffer, 256, Math.Min(255, remoteDir.Length));

                    serialPort1.Write(tBuffer, 0, tBuffer.Length);

                    // read command
                    Array.Clear(tBuffer, 0, tBuffer.Length);
                    curSize = 0;
                    while (curSize < 512) curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));

                    // read data
                    Array.Clear(tBuffer, 0, tBuffer.Length);
                    curSize = 0;
                    int type = 0x0;
                    string name;

                    // read directory listing packets
                    do
                    {
                        int bytesRead = serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));

                        if (bytesRead != 0)
                        {
                            curSize += bytesRead;

                            if (curSize % 512 == 0)
                            {
                                // parse strings
                                for (int i = 0; i < 512;)
                                {
                                    type = tBuffer[i++];

                                    if (type == 0 || type == 1)
                                    {
                                        name = "";

                                        while (tBuffer[i] != 0x0)
                                        {
                                            name += (char)tBuffer[i++];
                                        }
                                        i++;

                                        ListViewItem item = new ListViewItem(name, type);
                                        ListViewItem.ListViewSubItem[] subItems = new ListViewItem.ListViewSubItem[] { new ListViewItem.ListViewSubItem(item, type == 0 ? "Directory" : "File"), new ListViewItem.ListViewSubItem(item, "") };
                                        item.SubItems.AddRange(subItems);
                                        listViewRemote.Items.Add(item);
                                    }
                                    else if (type == 2 || type == 0xFF)
                                    {
                                        // continued on the next packet
                                        break;
                                    }
                                    else
                                    {
                                        throw new IndexOutOfRangeException();
                                    }
                                }
                            }
                        }
                    } while (type != 0xFF);

                    serialPort1.Close();

                    EnableButtons(true);
                    connected = true;
                }
            }
            catch (Exception x)
            {
                toolStripStatusLabel1.Text = x.Message.ToString();
                try { serialPort1.Close(); } catch (Exception x1) { }
                connected = false;
            }
        }

        private void RefreshListViewLocal()
        {
            try
            {
                listViewLocal.Clear();

                List<String> names = new List<String>(Directory.GetFileSystemEntries(localDir));
                names.Add(localDir + @"\..");

                foreach (string name in names)
                {
                    int type = (File.GetAttributes(name) & FileAttributes.Directory) == FileAttributes.Directory ? 0 : 1;
                    if (type == 1 && Path.GetExtension(name).ToLower() != ".sfc" && Path.GetExtension(name).ToLower() != ".smc" && Path.GetExtension(name).ToLower() != ".fig") continue;

                    ListViewItem item = new ListViewItem(System.IO.Path.GetFileName(name), type);
                    ListViewItem.ListViewSubItem[] subItems = new ListViewItem.ListViewSubItem[] { new ListViewItem.ListViewSubItem(item, type == 0 ? "Directory" : "File"), new ListViewItem.ListViewSubItem(item, "") };
                    item.SubItems.AddRange(subItems);
                    listViewLocal.Items.Add(item);
                }
            }
            catch (Exception x)
            {
                toolStripStatusLabel1.Text = x.Message.ToString();
            }
        }

        private void ConnectUSB()
        {
            Item item = (Item)comboBoxPort.SelectedItem;

            serialPort1.PortName = item.PortName;
            serialPort1.BaudRate = 9600;
            serialPort1.Parity = Parity.None;
            serialPort1.DataBits = 8;
            serialPort1.StopBits = StopBits.One;
            serialPort1.Handshake = Handshake.None;

            serialPort1.ReadTimeout = 500;
            serialPort1.WriteTimeout = 500;

            serialPort1.Open();
        }

        private void listViewRemote_SelectedIndexChanged(object sender, EventArgs e)
        {
            //if (e.Button == MouseButtons.Left)
            //{
            //    foreach (ListViewItem item in listViewRemote.SelectedItems)
            //    {
            //        if (item.ImageIndex == 0 && item.Text != ".")
            //        {
            //            if (item.Text == "..")
            //            {
            //                String[] elements = remoteDir.Split('/');
            //                elements = elements.Take(elements.Count() - 1).ToArray();
            //                remoteDir = String.Join("/", elements);
            //            }
            //            else
            //            {
            //                // directory
            //                remoteDir += '/' + item.Text;
            //            }
            //            RefreshListViewRemote();
            //        }
            //    }
            //}
        }

        private class Sorter : System.Collections.IComparer
        {
            public System.Windows.Forms.SortOrder Order = SortOrder.Ascending;

            public int Compare(object x, object y) // IComparer Member
            {
                if (!(x is ListViewItem))
                    return (0);
                if (!(y is ListViewItem))
                    return (0);

                ListViewItem l1 = (ListViewItem)x;
                ListViewItem l2 = (ListViewItem)y;

                if (l1.ImageIndex != l2.ImageIndex)
                {
                    if (Order == SortOrder.Ascending) return l1.ImageIndex.CompareTo(l2.ImageIndex);
                    else return l2.ImageIndex.CompareTo(l1.ImageIndex);
                }
                else
                {
                    if (Order == SortOrder.Ascending) return l1.Text.CompareTo(l2.Text);
                    else return l2.Text.CompareTo(l1.Text);
                }
            }
        }

        //private void listViewRemote_MouseDown(object sender, MouseEventArgs e)
        //{

        //    bootToolStripMenuItem.Enabled = false;
        //    makeDirToolStripMenuItem.Enabled = false;
        //    uploadToolStripMenuItem.Enabled = false;
        //    downloadToolStripMenuItem.Enabled = false;
        //    deleteToolStripMenuItem.Enabled = false;
        //    renameToolStripMenuItem.Enabled = false;

        //    if (connected)
        //    {
        //        var info = listViewRemote.HitTest(e.X, e.Y);

        //        uploadToolStripMenuItem.Enabled = true;
        //        makeDirToolStripMenuItem.Enabled = true;

        //        if (e.Button == MouseButtons.Right)
        //        {
        //            var loc = e.Location;
        //            loc.Offset(listViewRemote.Location);

        //            if (info.Item != null)
        //            {
        //                deleteToolStripMenuItem.Enabled = true;
        //                renameToolStripMenuItem.Enabled = true;

        //                if (info.Item.ImageIndex == 1)
        //                {
        //                    downloadToolStripMenuItem.Enabled = true;
        //                    bootToolStripMenuItem.Enabled = true;
        //                }
        //            }

        //            this.contextMenuStripRemote.Show(this, loc);
        //        }
        //    }
        //}

        private void listViewRemote_MouseDoubleClick(object sender, MouseEventArgs e)
        {

            if (e.Button == MouseButtons.Left)
            {
                if (connected)
                {
                    var info = listViewRemote.HitTest(e.X, e.Y);
                    if (info.Item != null)
                    {
                        if (info.Item.ImageIndex == 0 && info.Item.Text != ".")
                        {
                            remoteDirPrev = remoteDir;
                            if (info.Item.Text == "..")
                            {
                                String[] elements = remoteDir.Split('/');
                                elements = elements.Take(elements.Count() - 1).ToArray();
                                remoteDir = String.Join("/", elements);
                            }
                            else
                            {
                                // directory
                                remoteDir += '/' + info.Item.Text;
                            }
                            RefreshListViewRemote();
                            remoteDirNext = remoteDir;
                            backToolStripMenuItem.Enabled = true;
                            forwardToolStripMenuItem.Enabled = false;
                        }
                        else if (info.Item.ImageIndex == 1)
                        {
                            buttonBoot.PerformClick();
                        }
                    }
                }
            }
        }

        private void usb2snes_Load(object sender, EventArgs e)
        {
            localDir = Settings.Default.LocalDir;
            if (localDir == "" || !Directory.Exists(localDir)) localDir = System.IO.Directory.GetCurrentDirectory();
            localDirPrev = localDirNext = localDir;
            RefreshListViewLocal();

            // attempt to autoconnect
            buttonRefresh.PerformClick();
        }

        private void buttonMkdir_Click(object sender, EventArgs e)
        {
            try
            {
                if (connected)
                {
                    String dirName = Microsoft.VisualBasic.Interaction.InputBox("Remote Directory Name", "MkDir", "");
                    if (dirName != "")
                    {
                        string name = remoteDir + '/' + dirName;
                        if (name.Length < 256)
                        {
                            EnableButtons(false);

                            ConnectUSB();

                            byte[] tBuffer = new byte[512];

                            // send boot command
                            Array.Clear(tBuffer, 0, tBuffer.Length);
                            tBuffer[0] = Convert.ToByte('U');
                            tBuffer[1] = Convert.ToByte('S');
                            tBuffer[2] = Convert.ToByte('B');
                            tBuffer[3] = Convert.ToByte('A');
                            tBuffer[4] = Convert.ToByte(usbint_server_opcode_e.USBINT_SERVER_OPCODE_MKDIR); // opcode
                            tBuffer[5] = Convert.ToByte(usbint_server_space_e.USBINT_SERVER_SPACE_FILE); // space
                            tBuffer[6] = Convert.ToByte(usbint_server_flags_e.USBINT_SERVER_FLAGS_NONE); // flags

                            // directory
                            Buffer.BlockCopy(ASCIIEncoding.ASCII.GetBytes(name.ToArray()), 0, tBuffer, 256, name.Length);

                            serialPort1.Write(tBuffer, 0, tBuffer.Length);

                            // read response
                            Array.Clear(tBuffer, 0, tBuffer.Length);
                            int curSize = 0;
                            while (curSize < 512) curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));

                            serialPort1.Close();

                            RefreshListViewRemote();
                        }
                    }
                }
            }
            catch (Exception x)
            {
                toolStripStatusLabel1.Text = x.Message.ToString();
                try { serialPort1.Close(); } catch (Exception x1) { }
                connected = false;
            }
        }

        private void buttonRename_Click(object sender, EventArgs e)
        {
            try
            {
                if (connected)
                {
                    foreach (ListViewItem item in listViewRemote.SelectedItems)
                    {
                        string name = remoteDir + '/' + item.Text;
                        String newName = Microsoft.VisualBasic.Interaction.InputBox("New Name", "Rename", item.Text);

                        if (newName != "" && name.Length < 256 && newName.Length < 256 - 8)
                        {
                            EnableButtons(false);

                            ConnectUSB();

                            byte[] tBuffer = new byte[512];

                            // send boot command
                            Array.Clear(tBuffer, 0, tBuffer.Length);
                            tBuffer[0] = Convert.ToByte('U');
                            tBuffer[1] = Convert.ToByte('S');
                            tBuffer[2] = Convert.ToByte('B');
                            tBuffer[3] = Convert.ToByte('A');
                            tBuffer[4] = Convert.ToByte(usbint_server_opcode_e.USBINT_SERVER_OPCODE_MV); // opcode
                            tBuffer[5] = Convert.ToByte(usbint_server_space_e.USBINT_SERVER_SPACE_FILE); // space
                            tBuffer[6] = Convert.ToByte(usbint_server_flags_e.USBINT_SERVER_FLAGS_NONE); // flags

                            // directory
                            Buffer.BlockCopy(ASCIIEncoding.ASCII.GetBytes(name.ToArray()), 0, tBuffer, 256, name.Length);

                            // new name
                            Buffer.BlockCopy(ASCIIEncoding.ASCII.GetBytes(newName.ToArray()), 0, tBuffer, 8, newName.Length);

                            serialPort1.Write(tBuffer, 0, tBuffer.Length);

                            // read response
                            Array.Clear(tBuffer, 0, tBuffer.Length);
                            int curSize = 0;
                            while (curSize < 512) curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));

                            serialPort1.Close();
                        }
                    }

                    RefreshListViewRemote();
                }
            }
            catch (Exception x)
            {
                toolStripStatusLabel1.Text = x.Message.ToString();
                try { serialPort1.Close(); } catch (Exception x1) { }
                connected = false;
            }
        }

        private void buttonDelete_Click(object sender, EventArgs e)
        {
            try
            {
                if (connected)
                {
                    if (listViewRemote.SelectedItems.Count > 0)
                    {
                        DialogResult res = MessageBox.Show("OK to Delete: '" + listViewRemote.SelectedItems[0].Text + (listViewRemote.SelectedItems.Count > 1 ? "' and others" : "'") + "?", "Delete Message", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

                        if (res == DialogResult.Yes)
                        {
                            foreach (ListViewItem item in listViewRemote.SelectedItems)
                            {
                                string name = remoteDir + '/' + item.Text;
                                if (name.Length < 256)
                                {
                                    EnableButtons(false);

                                    ConnectUSB();

                                    byte[] tBuffer = new byte[512];

                                    // send boot command
                                    Array.Clear(tBuffer, 0, tBuffer.Length);
                                    tBuffer[0] = Convert.ToByte('U');
                                    tBuffer[1] = Convert.ToByte('S');
                                    tBuffer[2] = Convert.ToByte('B');
                                    tBuffer[3] = Convert.ToByte('A');
                                    tBuffer[4] = Convert.ToByte(usbint_server_opcode_e.USBINT_SERVER_OPCODE_RM); // opcode
                                    tBuffer[5] = Convert.ToByte(usbint_server_space_e.USBINT_SERVER_SPACE_FILE); // space
                                    tBuffer[6] = Convert.ToByte(usbint_server_flags_e.USBINT_SERVER_FLAGS_NONE); // flags

                                    // directory
                                    Buffer.BlockCopy(ASCIIEncoding.ASCII.GetBytes(name.ToArray()), 0, tBuffer, 256, name.Length);

                                    serialPort1.Write(tBuffer, 0, tBuffer.Length);

                                    // read response
                                    Array.Clear(tBuffer, 0, tBuffer.Length);
                                    int curSize = 0;
                                    while (curSize < 512) curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));

                                    serialPort1.Close();
                                }
                            }

                            RefreshListViewRemote();
                        }
                    }
                }
            }
            catch (Exception x)
            {
                toolStripStatusLabel1.Text = x.Message.ToString();
                try { serialPort1.Close(); } catch (Exception x1) { }
                connected = false;
            }

        }

        private void makeDirToolStripMenuItem_Click(object sender, EventArgs e)
        {
            buttonMkdir.PerformClick();
        }

        private void uploadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            buttonUpload.PerformClick();
        }

        private void downloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            buttonDownload.PerformClick();
        }

        private void bootToolStripMenuItem_Click(object sender, EventArgs e)
        {
            buttonBoot.PerformClick();
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            buttonDelete.PerformClick();
        }

        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            buttonRename.PerformClick();
        }

        private void listViewRemote_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                refreshToolStripMenuItem.Enabled = false;
                bootToolStripMenuItem.Enabled = false;
                makeDirToolStripMenuItem.Enabled = false;
                deleteToolStripMenuItem.Enabled = false;
                renameToolStripMenuItem.Enabled = false;

                if (connected)
                {
                    var info = listViewRemote.HitTest(e.X, e.Y);

                    refreshToolStripMenuItem.Enabled = true;
                    makeDirToolStripMenuItem.Enabled = true;

                    var loc = e.Location;
                    loc.Offset(listViewRemote.Location);

                    if (info.Item != null)
                    {
                        deleteToolStripMenuItem.Enabled = true;
                        renameToolStripMenuItem.Enabled = true;

                        if (info.Item.ImageIndex == 1)
                        {
                            bootToolStripMenuItem.Enabled = true;
                        }
                    }

                    this.contextMenuStripRemote.Show(this, loc);
                }
            }
        }

        private void backToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (remoteDir != remoteDirPrev)
            {
                // back
                remoteDirNext = remoteDir;
                remoteDir = remoteDirPrev;
                RefreshListViewRemote();
                backToolStripMenuItem.Enabled = false;
                forwardToolStripMenuItem.Enabled = true;
            }

        }

        private void forwardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // forward
            if (remoteDir != remoteDirNext)
            {
                remoteDirPrev = remoteDir;
                remoteDir = remoteDirNext;
                RefreshListViewRemote();
                backToolStripMenuItem.Enabled = true;
                forwardToolStripMenuItem.Enabled = false;
            }
        }

        private void listViewLocal_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var info = listViewLocal.HitTest(e.X, e.Y);
                if (info.Item != null)
                {
                    if (info.Item.ImageIndex == 0 && info.Item.Text != ".")
                    {
                        localDirPrev = localDir;
                        if (info.Item.Text == "..")
                        {
                            try
                            {
                                String dir = System.IO.Path.GetDirectoryName(localDir);
                                if (System.IO.Path.IsPathRooted(dir)) localDir = dir;
                            }
                            catch (Exception x)
                            {

                            }
                        }
                        else
                        {
                            // directory
                            localDir += @"\" + info.Item.Text;
                        }
                        RefreshListViewLocal();
                        localDirNext = localDir;
                        backToolStripMenuItem1.Enabled = true;
                        forwardToolStripMenuItem1.Enabled = false;
                    }
                    else if (info.Item.ImageIndex == 1)
                    {
                        buttonBoot.PerformClick();
                    }
                }
            }
        }

        private void usb2snes_FormClosed(object sender, FormClosedEventArgs e)
        {
            Settings.Default.LocalDir = localDir;
            Settings.Default.Save();
        }

        private void contextMenuStripRemote_Opening(object sender, CancelEventArgs e)
        {

        }

        private void backToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (localDir != localDirPrev)
            {
                // back
                localDirNext = localDir;
                localDir = localDirPrev;
                RefreshListViewLocal();
                backToolStripMenuItem1.Enabled = false;
                forwardToolStripMenuItem1.Enabled = true;
            }


        }

        private void forwardToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            // forward
            if (localDir != localDirNext)
            {
                localDirPrev = localDir;
                localDir = localDirNext;
                RefreshListViewLocal();
                backToolStripMenuItem.Enabled = true;
                forwardToolStripMenuItem.Enabled = false;
            }

        }

        private void listViewLocal_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                refreshToolStripMenuItem1.Enabled = false;
                makeDirToolStripMenuItem1.Enabled = false;
                //deleteToolStripMenuItem1.Enabled = false;
                renameToolStripMenuItem1.Enabled = false;

                {
                    var info = listViewLocal.HitTest(e.X, e.Y);

                    makeDirToolStripMenuItem1.Enabled = true;
                    refreshToolStripMenuItem1.Enabled = true;

                    var loc = e.Location;
                    loc.Offset(listViewLocal.Location);

                    if (info.Item != null)
                    {
                        //deleteToolStripMenuItem1.Enabled = true;
                        renameToolStripMenuItem1.Enabled = true;
                    }

                    this.contextMenuStripLocal.Show(this, loc);
                }
            }

        }

        private void makeDirToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            String dirName = Microsoft.VisualBasic.Interaction.InputBox("Local Directory Name", "MkDir", "");
            if (dirName != "")
            {
                string name = localDir + '/' + dirName;
                if (name.Length < 256)
                {
                    Directory.CreateDirectory(name);
                    RefreshListViewLocal();
                }
            }

        }

        private void renameToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in listViewLocal.SelectedItems)
            {
                string name = localDir + @"\" + item.Text;
                String newName = Microsoft.VisualBasic.Interaction.InputBox("New Name", "Rename", item.Text);

                if (newName != "")
                {
                    File.Move(name, localDir + @"\" + newName);
                }
            }

            RefreshListViewLocal();

        }

        private void usb2snes_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                RefreshListViewLocal();
                RefreshListViewRemote();
            }
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RefreshListViewRemote();
        }

        private void refreshToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            RefreshListViewLocal();
        }

        private void buttonPatch_Click(object sender, EventArgs e)
        {
            try
            {
                if (connected)
                {
                    openFileDialog1.Title = "RAM IPS file to load";
                    openFileDialog1.Filter = "IPS File|*.ips"
                                           + "|All Files|*.*";
                    openFileDialog1.FileName = "";

                    if (openFileDialog1.ShowDialog() != DialogResult.Cancel)
                    {
                        //if (listViewLocal.SelectedItems.Count > 0)
                        //{
                        EnableButtons(false);

                        ConnectUSB();

                        for (int i = 0; i < openFileDialog1.FileNames.Length; i++)
                        //foreach (ListViewItem item in listViewLocal.SelectedItems)
                        {
                            string fileName = openFileDialog1.FileNames[i];
                            string safeFileName = openFileDialog1.SafeFileNames[i];

                            applyPatch(fileName, safeFileName);
                        }

                        System.Threading.Thread.Sleep(100);

                        serialPort1.Close();

                        EnableButtons(true);
                        //RefreshListViewRemote();
                    }
                }
            }
            catch (Exception x)
            {
                toolStripStatusLabel1.Text = x.Message.ToString();
                try { serialPort1.Close(); } catch (Exception x1) { }
                connected = false;
            }
        }

        private class IPS
        {
            public IPS() { Items = new List<Patch>(); }

            public class Patch
            {
                public Patch() { data = new List<Byte>(); }

                public int        address; // 24b file address
                public List<Byte> data;
            }

            public List<Patch> Items;

            public void Parse(string fileName)
            {
                int index = 0;

                FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                // make sure the first few characters match string
                byte[] buffer = new byte[512];

                System.Text.UTF8Encoding enc = new System.Text.UTF8Encoding();
                fs.Read(buffer, 0, 5);
                for (int i = 0; i < 5; i++)
                {
                    if (buffer[i] != enc.GetBytes("PATCH")[i])
                        throw new Exception("IPS: error parsing PATCH");
                }
                index += 5;

                bool foundEOF = false;
                while (!foundEOF)
                {
                    int bytesRead = 0;

                    // read address
                    bytesRead = fs.Read(buffer, 0, 3);
                    // check EOF
                    if (index == fs.Length - 3 || index == fs.Length - 6)
                    {
                        foundEOF = true;
                        // check for EOF
                        for (int i = 0; i < 3; i++)
                        {
                            if (buffer[i] != enc.GetBytes("EOF")[i])
                            {
                                foundEOF = false;
                                break;
                            }
                        }
                    }

                    if (!foundEOF)
                    {
                        Patch patch = new Patch();
                        Items.Add(patch);

                        // get address
                        if (bytesRead != 3) throw new Exception("IPS: error parsing address");
                        patch.address = buffer[0]; patch.address <<= 8;
                        patch.address |= buffer[1]; patch.address <<= 8;
                        patch.address |= buffer[2]; patch.address <<= 0;
                        index += bytesRead;

                        // get length
                        bytesRead = fs.Read(buffer, 0, 2);
                        if (bytesRead != 2) throw new Exception("IPS: error parsing length");
                        int length = buffer[0]; length <<= 8;
                        length |= buffer[1]; length <<= 0;
                        index += bytesRead;

                        // check if RLE
                        if (length == 0)
                        {
                            // RLE
                            bytesRead = fs.Read(buffer, 0, 3);
                            if (bytesRead != 3) throw new Exception("IPS: error parsing RLE count/byte");
                            int count = buffer[0]; count <<= 8;
                            count |= buffer[1]; count <<= 0;
                            Byte val = buffer[2];
                            index += bytesRead;

                            patch.data.AddRange(Enumerable.Repeat(val, count));
                        }
                        else
                        {
                            int count = 0;
                            while (count < length)
                            {
                                bytesRead = fs.Read(buffer, 0, Math.Min(buffer.Length, length - count));
                                if (bytesRead == 0) throw new Exception("IPS: error parsing data");
                                count += bytesRead;
                                index += bytesRead;
                                patch.data.AddRange(buffer.Take(bytesRead));
                            }
                        }
                    }
                }

                // ignore truncation
                if (index != fs.Length && index != fs.Length - 3)
                    throw new Exception("IPS: unexpected end of file");

                fs.Close();
            }
        }

        private void applyPatch(string fileName, string safeFileName)
        {
            for (int i = 0; i < openFileDialog1.FileNames.Length; i++)
            {
                IPS ips = new IPS();
                ips.Parse(fileName);

                //{
                //FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);

                byte[] tBuffer = new byte[512];
                int curSize = 0;

                // send write command
                int patchNum = 0;
                foreach (var patch in ips.Items)
                {
                    Array.Clear(tBuffer, 0, tBuffer.Length);
                    tBuffer[0] = Convert.ToByte('U');
                    tBuffer[1] = Convert.ToByte('S');
                    tBuffer[2] = Convert.ToByte('B');
                    tBuffer[3] = Convert.ToByte('A');
                    tBuffer[4] = Convert.ToByte(usbint_server_opcode_e.USBINT_SERVER_OPCODE_PUT); // opcode
                    tBuffer[5] = Convert.ToByte(usbint_server_space_e.USBINT_SERVER_SPACE_SNES); // space
                    tBuffer[6] = Convert.ToByte((patchNum == 0 && i == 0) ? usbint_server_flags_e.USBINT_SERVER_FLAGS_CLRX
                                               : (patchNum == ips.Items.Count - 1 && i == openFileDialog1.FileNames.Length - 1) ? usbint_server_flags_e.USBINT_SERVER_FLAGS_SETX
                                               : usbint_server_flags_e.USBINT_SERVER_FLAGS_NONE);

                    long fileSize = patch.data.Count;
                    tBuffer[252] = Convert.ToByte((fileSize >> 24) & 0xFF);
                    tBuffer[253] = Convert.ToByte((fileSize >> 16) & 0xFF);
                    tBuffer[254] = Convert.ToByte((fileSize >> 8) & 0xFF);
                    tBuffer[255] = Convert.ToByte((fileSize >> 0) & 0xFF);

                    // temp offset
                    //tBuffer[256] = 0x00;
                    //tBuffer[257] = 0x20;
                    //tBuffer[258] = 0x00;
                    //tBuffer[259] = 0x00;
                    tBuffer[256] = Convert.ToByte((patch.address >> 24) & 0xFF);
                    tBuffer[257] = Convert.ToByte((patch.address >> 16) & 0xFF);
                    tBuffer[258] = Convert.ToByte((patch.address >> 8) & 0xFF);
                    tBuffer[259] = Convert.ToByte((patch.address >> 0) & 0xFF);

                    System.Threading.Thread.Sleep(100);
                    serialPort1.Write(tBuffer, 0, tBuffer.Length);

                    // read info
                    Array.Clear(tBuffer, 0, tBuffer.Length);
                    curSize = 0;
                    System.Threading.Thread.Sleep(100);
                    while (curSize < 512) curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));

                    // write data
                    Array.Clear(tBuffer, 0, tBuffer.Length);
                    curSize = 0;
                    toolStripProgressBar1.Value = 0;
                    toolStripProgressBar1.Enabled = true;
                    toolStripStatusLabel1.Text = "uploading ram: " + safeFileName;

                    System.Threading.Thread.Sleep(100);
                    while (curSize < patch.data.Count)
                    {
                        int bytesToWrite = Math.Min(512, patch.data.Count - curSize);
                        Array.Copy(patch.data.ToArray(), curSize, tBuffer, 0, bytesToWrite);
                        serialPort1.Write(tBuffer, 0, tBuffer.Length);
                        curSize += bytesToWrite;
                        toolStripProgressBar1.Value = 100 * curSize / patch.data.Count;
                    }
                    toolStripStatusLabel1.Text = "idle";
                    toolStripProgressBar1.Enabled = false;

                    patchNum++;
                }
                //fs.Close();
            }
        }

        private void buttonGetState_Click(object sender, EventArgs e)
        {
            try
            {
                if (connected)
                {
                    EnableButtons(false);

                    ConnectUSB();

                    saveFileDialog1.Title = "State file to Save";
                    saveFileDialog1.Filter = "STATE File|*.ss0"
                                           + "|All Files|*.*";
                    saveFileDialog1.FileName = "save.ss0";

                    if (saveFileDialog1.ShowDialog() != DialogResult.Cancel)
                    {
                        FileStream fs = new FileStream(saveFileDialog1.FileName, FileMode.Create, FileAccess.Write);
                        //BinaryWriter bs = new BinaryWriter(fs);

                        byte[] tBuffer = new byte[512];
                        int curSize = 0;

                        // send read command
                        Array.Clear(tBuffer, 0, tBuffer.Length);
                        tBuffer[0] = Convert.ToByte('U');
                        tBuffer[1] = Convert.ToByte('S');
                        tBuffer[2] = Convert.ToByte('B');
                        tBuffer[3] = Convert.ToByte('A');
                        tBuffer[4] = Convert.ToByte(usbint_server_opcode_e.USBINT_SERVER_OPCODE_GET); // opcode
                        tBuffer[5] = Convert.ToByte(usbint_server_space_e.USBINT_SERVER_SPACE_SNES); // space
                        tBuffer[6] = Convert.ToByte(usbint_server_flags_e.USBINT_SERVER_FLAGS_NONE); // flags

                        // size - 256KB
                        tBuffer[252] = 0x00;
                        tBuffer[253] = 0x04;
                        tBuffer[254] = 0x00;
                        tBuffer[255] = 0x00;

                        // patch region
                        tBuffer[256] = 0x00;
                        tBuffer[257] = 0xF0;
                        tBuffer[258] = 0x00;
                        tBuffer[259] = 0x00;

                        serialPort1.Write(tBuffer, 0, tBuffer.Length);

                        // read response
                        Array.Clear(tBuffer, 0, tBuffer.Length);
                        curSize = 0;
                        while (curSize < 512) curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));
                        int fileSize = 0;
                        fileSize |= tBuffer[252]; fileSize <<= 8;
                        fileSize |= tBuffer[253]; fileSize <<= 8;
                        fileSize |= tBuffer[254]; fileSize <<= 8;
                        fileSize |= tBuffer[255]; fileSize <<= 0;

                        // read data
                        Array.Clear(tBuffer, 0, tBuffer.Length);
                        curSize = 0;
                        toolStripProgressBar1.Value = 0;
                        toolStripProgressBar1.Enabled = true;
                        toolStripStatusLabel1.Text = "downloading: " + saveFileDialog1.FileName;
                        while (curSize < fileSize)
                        {
                            int prevSize = curSize;
                            curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));
                            fs.Write(tBuffer, (prevSize % 512), curSize - prevSize);
                            toolStripProgressBar1.Value = 100 * curSize / fileSize;
                        }
                        toolStripStatusLabel1.Text = "idle";
                        toolStripProgressBar1.Enabled = false;

                        fs.Close();

                        System.Threading.Thread.Sleep(100);

                        serialPort1.Close();

                        RefreshListViewRemote();
                        RefreshListViewLocal();
                    }
                }
            }
            catch (Exception x)
            {
                toolStripStatusLabel1.Text = x.Message.ToString();
                try { serialPort1.Close(); } catch (Exception x1) { }
                connected = false;
            }
        }


        private void buttonSetState_Click(object sender, EventArgs e)
        {
            try
            {
                if (connected)
                {
                    openFileDialog1.Title = "State file to Load";
                    openFileDialog1.Filter = "STATE File|*.ss0"
                                           + "|All Files|*.*";
                    openFileDialog1.FileName = "save.ss0";

                    if (openFileDialog1.ShowDialog() != DialogResult.Cancel)
                    {
                    //if (listViewLocal.SelectedItems.Count > 0)
                    //{
                        EnableButtons(false);

                        ConnectUSB();

                        for (int i = 0; i < openFileDialog1.FileNames.Length; i++)
                        //foreach (ListViewItem item in listViewLocal.SelectedItems)
                        {
                            string fileName = openFileDialog1.FileNames[i];
                            string safeFileName = openFileDialog1.SafeFileNames[i];

                            //{
                            FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);

                            byte[] tBuffer = new byte[512];
                            int curSize = 0;

                            // send write command
                            Array.Clear(tBuffer, 0, tBuffer.Length);
                            tBuffer[0] = Convert.ToByte('U');
                            tBuffer[1] = Convert.ToByte('S');
                            tBuffer[2] = Convert.ToByte('B');
                            tBuffer[3] = Convert.ToByte('A');
                            tBuffer[4] = Convert.ToByte(usbint_server_opcode_e.USBINT_SERVER_OPCODE_PUT); // opcode
                            tBuffer[5] = Convert.ToByte(usbint_server_space_e.USBINT_SERVER_SPACE_SNES); // space
                            tBuffer[6] = Convert.ToByte(usbint_server_flags_e.USBINT_SERVER_FLAGS_NONE); // flags

                            long fileSize = new FileInfo(fileName).Length;
                            tBuffer[252] = Convert.ToByte((fileSize >> 24) & 0xFF);
                            tBuffer[253] = Convert.ToByte((fileSize >> 16) & 0xFF);
                            tBuffer[254] = Convert.ToByte((fileSize >> 8) & 0xFF);
                            tBuffer[255] = Convert.ToByte((fileSize >> 0) & 0xFF);

                            // patch region
                            tBuffer[256] = 0x00;
                            tBuffer[257] = 0xF0;
                            tBuffer[258] = 0x00;
                            tBuffer[259] = 0x00;

                            serialPort1.Write(tBuffer, 0, tBuffer.Length);

                            // read info
                            Array.Clear(tBuffer, 0, tBuffer.Length);
                            curSize = 0;
                            while (curSize < 512) curSize += serialPort1.Read(tBuffer, (curSize % 512), 512 - (curSize % 512));

                            // write data
                            Array.Clear(tBuffer, 0, tBuffer.Length);
                            curSize = 0;
                            toolStripProgressBar1.Value = 0;
                            toolStripProgressBar1.Enabled = true;
                            toolStripStatusLabel1.Text = "uploading: " + safeFileName;
                            while (curSize < fs.Length)
                            {
                                int bytesToWrite = fs.Read(tBuffer, 0, 512);
                                serialPort1.Write(tBuffer, 0, bytesToWrite);
                                curSize += bytesToWrite;
                                toolStripProgressBar1.Value = 100 * curSize / (int)fs.Length;
                            }
                            toolStripStatusLabel1.Text = "idle";
                            toolStripProgressBar1.Enabled = false;

                            fs.Close();
                        }

                        System.Threading.Thread.Sleep(100);

                        serialPort1.Close();

                        RefreshListViewRemote();
                    }
                }
            }
            catch (Exception x)
            {
                toolStripStatusLabel1.Text = x.Message.ToString();
                try { serialPort1.Close(); } catch (Exception x1) { }
                connected = false;
            }

        }
    }
}
