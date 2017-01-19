using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Forms;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Diagnostics;

namespace MessageTracking
{
    public partial class FormMain : Form
    {
        public Runspace _exchangeRunspace = null;
        public Runspace _o365Runspace = null;
        string _userSMTPAddress = String.Empty;
        string _userid = String.Empty;
        public List<string> dlList = new List<string>();
        string PowerShellUri = "";
        string O365Uri = "https://outlook.office365.com/powershell-liveid";
        public ArrayList allExchangeServersInExchangeServersGroup = new ArrayList();
        public ArrayList selectedExchangeServers = new ArrayList();
        private ColumnHeader SortingColumn = null;
        int globalCounter = 1;


        public FormMain()
        {
            InitializeComponent();
            startDate.Value = startDate.Value.AddHours(-1);
            checkEnvironment();
        }

        private void checkEnvironment()
        {
            string domainName = "NODOMAIN";
            try
            {
                domainName = System.DirectoryServices.ActiveDirectory.Domain.GetComputerDomain().Name;

                PrincipalContext ctx = new PrincipalContext(ContextType.Domain);

                GroupPrincipal exchangeServersGroup = GroupPrincipal.FindByIdentity(ctx, "Exchange Servers");

                if(exchangeServersGroup != null)
                {
                    foreach(Principal p in exchangeServersGroup.GetMembers())
                    {
                        if(p.StructuralObjectClass == "computer")
                        {
                            allExchangeServersInExchangeServersGroup.Add(p.Name);
                        }
                    }
                }

                allExchangeServersInExchangeServersGroup.Sort();

                checkBoxOnPrem.Checked = true;

                foreach (string exServer in allExchangeServersInExchangeServersGroup)
                {
                  //  if (selectedExchangeServers.Contains(exServer))
                        exchangeServersCheckedListBox.Items.Add(exServer, true);
                   // else
                    //    exchangeServersCheckedListBox.Items.Add(exServer, false);
                }

                ComputerPrincipal remotePowerShellServer = ComputerPrincipal.FindByIdentity(ctx, (allExchangeServersInExchangeServersGroup[allExchangeServersInExchangeServersGroup.Count - 1]).ToString());
                DirectoryEntry remotePSServer = remotePowerShellServer.GetUnderlyingObject() as DirectoryEntry;
                textBoxRemotePowerShell.Text = remotePSServer.Properties["dNSHostName"].Value.ToString();

                PowerShellUri = "http://" + remotePSServer.Properties["dNSHostName"].Value.ToString() + "/PowerShell";

            }
            catch {
                checkBoxOffice365.Select();
                checkBoxOnPrem.Enabled = false;
            }
        }
        private void closeOpenConnections()
        {
            if (_exchangeRunspace != null)
            {
                if (_exchangeRunspace.RunspaceStateInfo.State == RunspaceState.Opened)
                {
                    try
                    {
                        _exchangeRunspace.Close();
                    }
                    catch { }
                }
            }

            if (_o365Runspace != null)
            {
                if (_o365Runspace.RunspaceStateInfo.State == RunspaceState.Opened)
                {
                    try
                    {
                        _o365Runspace.Close();
                    }
                    catch { }
                }
            }
        }

        public bool ConnectWithRemotePowerShell()
        {
            if (_exchangeRunspace != null)
            {
                if (_exchangeRunspace.RunspaceStateInfo.State == RunspaceState.Opened)
                    return true;
            }
            try
            {
                WSManConnectionInfo ConnInfo = new WSManConnectionInfo(new Uri(PowerShellUri));
                ConnInfo.ShellUri = "http://schemas.microsoft.com/powershell/Microsoft.Exchange";
                ConnInfo.AuthenticationMechanism = AuthenticationMechanism.Kerberos;
                _exchangeRunspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(ConnInfo);
                _exchangeRunspace.Open();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public bool ConnectWithO365PowerShell()
        {
            if (_o365Runspace != null)
            {
                if (_o365Runspace.RunspaceStateInfo.State == RunspaceState.Opened)
                    return true;
            }
            try
            {
                System.Security.SecureString securePassword = new System.Security.SecureString();
                foreach (char c in textBoxPassword.Text)
                {
                    securePassword.AppendChar(c);
                }
                PSCredential cred = new PSCredential(textBoxUserName.Text, securePassword);
                WSManConnectionInfo ConnInfo = new WSManConnectionInfo(new Uri(O365Uri));
                if (checkBoxIEProxy.Checked)
                {
                    ConnInfo.ProxyAccessType = System.Management.Automation.Remoting.ProxyAccessType.IEConfig;
                    ConnInfo.ProxyAuthentication = AuthenticationMechanism.Negotiate;
                }
                ConnInfo.ShellUri = "http://schemas.microsoft.com/powershell/Microsoft.Exchange";
                ConnInfo.AuthenticationMechanism = AuthenticationMechanism.Basic;
                ConnInfo.Credential = cred;
                _o365Runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(ConnInfo);
                _o365Runspace.Open();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());

                return false;
            }
        }

        Collection<object> InvokeCommand(PowerShell powershell)
        {
            Collection<object> result = null;
            result = powershell.Invoke<object>();

            return result;
        }

        private void RetrieveO365SearchResults()
        {
            if (!ConnectWithO365PowerShell())
                return;

            ClassWaitSplashScreen ws = new ClassWaitSplashScreen(this);
            ws.Show("Retrieving Exchange Online message tracking results, please wait...");

            PowerShell powershell = PowerShell.Create();
            PSCommand command = new PSCommand();
            command.AddCommand("Get-MessageTrace");
            command.AddParameter("StartDate", startDate.Value);
            command.AddParameter("EndDate", endDate.Value);
            if (!string.IsNullOrWhiteSpace(textBoxSender.Text))
            {
                command.AddParameter("SenderAddress", textBoxSender.Text);
            }
            if (!string.IsNullOrWhiteSpace(textBoxRecipient.Text))
            {
                command.AddParameter("RecipientAddress", textBoxRecipient.Text);
            }

            if (!string.IsNullOrWhiteSpace(textBoxMessageID.Text))
            {
                command.AddParameter("MessageId", textBoxMessageID.Text);
            }

            powershell.Commands = command;
            powershell.Runspace = _o365Runspace;
            Collection<object> trackingPowerShellResults = InvokeCommand(powershell);

            if (trackingPowerShellResults != null)
            {
                int i = 1;
                foreach (PSObject result in trackingPowerShellResults)
                {
                    if (!string.IsNullOrWhiteSpace(textBoxSubject.Text))
                    {
                        if (result.Properties["Subject"].Value.ToString() == textBoxSubject.Text)
                        {
                            string[] row1 = { result.Properties["Status"].Value.ToString() + " (EXO)", result.Properties["SenderAddress"].Value.ToString(), result.Properties["RecipientAddress"].Value.ToString(), result.Properties["Subject"].Value.ToString(), result.Properties["Received"].Value.ToString(), result.Properties["Size"].Value.ToString(), result.Properties["MessageID"].Value.ToString(), "" };
                            listViewResults.Items.Add(globalCounter.ToString()).SubItems.AddRange(row1);
                            globalCounter++;
                        }
                    }
                    else
                    {
                        string[] row1 = { result.Properties["Status"].Value.ToString() + " (EXO)", result.Properties["SenderAddress"].Value.ToString(), result.Properties["RecipientAddress"].Value.ToString(), result.Properties["Subject"].Value.ToString(), result.Properties["Received"].Value.ToString(), result.Properties["Size"].Value.ToString(), result.Properties["MessageID"].Value.ToString(), "" };
                        listViewResults.Items.Add(globalCounter.ToString()).SubItems.AddRange(row1);
                        globalCounter++;
                    }
                }
            }

            ws.Close();


        }

        private void RetrieveOnPremSearchResults()
        {
            if (!ConnectWithRemotePowerShell())
                return;
            
            ClassWaitSplashScreen ws = new ClassWaitSplashScreen(this);
            ws.Show("Retrieving on-prem message tracking results, please wait...");

            //string[] exchangeServerList = allExchangeServersInExchangeServersGroup.ToArray();

            int i = 1;
            ArrayList messageIDList = new ArrayList();

            foreach (string exchangeServer in selectedExchangeServers)
            {
                PowerShell powershell = PowerShell.Create();
                PSCommand command = new PSCommand();
                command.AddCommand("Get-MessageTrackingLog");
                command.AddParameter("Start", startDate.Value);
                command.AddParameter("End", endDate.Value);
                if (!string.IsNullOrWhiteSpace(textBoxSender.Text))
                {
                    command.AddParameter("Sender", textBoxSender.Text);
                }
                command.AddParameter("Server", exchangeServer);

                if (!string.IsNullOrWhiteSpace(textBoxRecipient.Text))
                {
                    command.AddParameter("Recipients", textBoxRecipient.Text);
                }

                if (!string.IsNullOrWhiteSpace(textBoxSubject.Text))
                {
                    command.AddParameter("MessageSubject", textBoxSubject.Text);
                }

                if (!string.IsNullOrWhiteSpace(textBoxMessageID.Text))
                {
                    command.AddParameter("MessageId", textBoxMessageID.Text);
                }

                powershell.Commands = command;
                powershell.Runspace = _exchangeRunspace;
                Collection<object> trackingPowerShellResults = InvokeCommand(powershell);

                if (trackingPowerShellResults != null)
                {
                    foreach (PSObject result in trackingPowerShellResults)
                    {
                        if ((result.Properties["Source"].Value.ToString() == "STOREDRIVER" && result.Properties["EventId"].Value.ToString() == "DELIVER"))
                        {
                           // if (!messageIDList.Contains(result.Properties["MessageId"].Value.ToString()))
                           // {
                             //   messageIDList.Add(result.Properties["MessageId"].Value.ToString());

                                try
                                {
                                    string[] row1 = { "Delivered Locally", result.Properties["Sender"].Value.ToString(), result.Properties["Recipients"].Value.ToString(), result.Properties["MessageSubject"].Value.ToString(), result.Properties["Timestamp"].Value.ToString(), result.Properties["TotalBytes"].Value.ToString(), result.Properties["MessageId"].Value.ToString(), result.Properties["MessageLatency"].Value.ToString() };
                                    listViewResults.Items.Add(globalCounter.ToString()).SubItems.AddRange(row1);
                                    globalCounter++;
                                }

                                catch { }
                          //  }
                        }

                        if ((result.Properties["Source"].Value.ToString() == "SMTP" && result.Properties["EventId"].Value.ToString() == "SEND" && result.Properties["ConnectorId"].Value.ToString() != "Intra-Organization SMTP Send Connector"))
                        {
                            try
                            {
                                string[] row1 = { "Sent by SMTP", result.Properties["Sender"].Value.ToString(), result.Properties["Recipients"].Value.ToString(), result.Properties["MessageSubject"].Value.ToString(), result.Properties["Timestamp"].Value.ToString(), result.Properties["TotalBytes"].Value.ToString(), result.Properties["MessageId"].Value.ToString(), result.Properties["MessageLatency"].Value.ToString() };
                                listViewResults.Items.Add(globalCounter.ToString()).SubItems.AddRange(row1);
                                globalCounter++;
                            }

                            catch { }
                        }

                        if (result.Properties["EventId"].Value.ToString() == "DSN")
                        {
                            try
                            {
                                string[] row1 = { "DSN", result.Properties["Sender"].Value.ToString(), result.Properties["Recipients"].Value.ToString(), result.Properties["MessageSubject"].Value.ToString(), result.Properties["Timestamp"].Value.ToString(), result.Properties["TotalBytes"].Value.ToString(), result.Properties["MessageId"].Value.ToString(), result.Properties["MessageLatency"].Value.ToString() };
                                listViewResults.Items.Add(globalCounter.ToString()).SubItems.AddRange(row1);
                                globalCounter++;
                            }

                            catch { }
                        }

                        if (result.Properties["EventId"].Value.ToString() == "FAIL")
                        {
                            try
                            {
                                string[] row1 = { "FAIL", result.Properties["Sender"].Value.ToString(), result.Properties["Recipients"].Value.ToString(), result.Properties["MessageSubject"].Value.ToString(), result.Properties["Timestamp"].Value.ToString(), result.Properties["TotalBytes"].Value.ToString(), result.Properties["MessageId"].Value.ToString(), result.Properties["MessageLatency"].Value.ToString() };
                                listViewResults.Items.Add(globalCounter.ToString()).SubItems.AddRange(row1);
                                globalCounter++;
                            }

                            catch { }
                        }

                        if (result.Properties["EventId"].Value.ToString() == "POISONMESSAGE")
                        {
                            try
                            {
                                string[] row1 = { "POISON MESSAGE", result.Properties["Sender"].Value.ToString(), result.Properties["Recipients"].Value.ToString(), result.Properties["MessageSubject"].Value.ToString(), result.Properties["Timestamp"].Value.ToString(), result.Properties["TotalBytes"].Value.ToString(), result.Properties["MessageId"].Value.ToString(), result.Properties["MessageLatency"].Value.ToString() };
                                listViewResults.Items.Add(globalCounter.ToString()).SubItems.AddRange(row1);
                                globalCounter++;
                            }

                            catch { }
                        }

                        if (result.Properties["EventId"].Value.ToString() == "SUBMITFAIL")
                        {
                            try
                            {
                                string[] row1 = { "SUBMIT FAIL", result.Properties["Sender"].Value.ToString(), result.Properties["Recipients"].Value.ToString(), result.Properties["MessageSubject"].Value.ToString(), result.Properties["Timestamp"].Value.ToString(), result.Properties["TotalBytes"].Value.ToString(), result.Properties["MessageId"].Value.ToString(), result.Properties["MessageLatency"].Value.ToString() };
                                listViewResults.Items.Add(globalCounter.ToString()).SubItems.AddRange(row1);
                                globalCounter++;
                            }

                            catch { }
                        }

                        if (result.Properties["EventId"].Value.ToString() == "DEFER")
                        {
                            try
                            {
                                string[] row1 = { "DELAYED", result.Properties["Sender"].Value.ToString(), result.Properties["Recipients"].Value.ToString(), result.Properties["MessageSubject"].Value.ToString(), result.Properties["Timestamp"].Value.ToString(), result.Properties["TotalBytes"].Value.ToString(), result.Properties["MessageId"].Value.ToString(), result.Properties["MessageLatency"].Value.ToString() };
                                listViewResults.Items.Add(globalCounter.ToString()).SubItems.AddRange(row1);
                                globalCounter++;
                            }

                            catch { }
                        }
                    }
                }
            }
            ws.Close();
        }


        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string lineText = "\"Status\",\"Sender\",\"Recipient\",\"Subject\",\"ReceivedDate\",\"Size\",\"MessageID\",\"MessageLatency\"" + Environment.NewLine;

            foreach (ListViewItem item in listViewResults.SelectedItems)
            {
                lineText += "\"" + item.SubItems[1].Text + "\",\"" + item.SubItems[2].Text + "\",\"" + item.SubItems[3].Text + "\",\"" + item.SubItems[4].Text + "\",\"" + item.SubItems[5].Text + "\",\"" + item.SubItems[6].Text + "\",\"" + item.SubItems[7].Text + "\",\"" + item.SubItems[8].Text + "\"" + Environment.NewLine;
            }
            Clipboard.SetText(lineText);
        }

        private void saveAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string lineText = "\"Status\",\"Sender\",\"Recipient\",\"Subject\",\"ReceivedDate\",\"Size\",\"MessageID\",\"MessageLatency\"" + Environment.NewLine;

            foreach (ListViewItem item in listViewResults.Items)
            {
                lineText += "\"" + item.SubItems[1].Text + "\",\"" + item.SubItems[2].Text + "\",\"" + item.SubItems[3].Text + "\",\"" + item.SubItems[4].Text + "\",\"" + item.SubItems[5].Text + "\",\"" + item.SubItems[6].Text + "\",\"" + item.SubItems[7].Text + "\",\"" + item.SubItems[8].Text + "\"" + Environment.NewLine;
            }

            SaveFileDialog saveAs = new SaveFileDialog();
            saveAs.Filter = "Csv file (*.csv)|*.csv";

            if (saveAs.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllText(saveAs.FileName, lineText);
            }
        }
      

        private void buttonSearch_Click_1(object sender, EventArgs e)
        {
            listViewResults.Items.Clear();
            globalCounter = 1;

            if (checkBoxOnPrem.Checked && (!string.IsNullOrWhiteSpace(textBoxSender.Text) || !string.IsNullOrWhiteSpace(textBoxRecipient.Text) || !string.IsNullOrWhiteSpace(textBoxMessageID.Text)))
            {
                RetrieveOnPremSearchResults();
            }
            if (string.IsNullOrWhiteSpace(textBoxSender.Text) && string.IsNullOrWhiteSpace(textBoxRecipient.Text) && string.IsNullOrWhiteSpace(textBoxMessageID.Text))
            {
                System.Windows.Forms.MessageBox.Show(this, "You need to provide a value for at least one of the missing query details: Sender, Recipient or Message ID", "Mandatory Field Missing", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            if (checkBoxOffice365.Checked && (string.IsNullOrWhiteSpace(textBoxUserName.Text) || string.IsNullOrWhiteSpace(textBoxPassword.Text)))
            {
                System.Windows.Forms.MessageBox.Show(this, "Username and Password is mandatory for Office 365 queries", "Mandatory Field Missing", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            if (checkBoxOffice365.Checked && !string.IsNullOrWhiteSpace(textBoxUserName.Text) && !string.IsNullOrWhiteSpace(textBoxPassword.Text) && (!string.IsNullOrWhiteSpace(textBoxSender.Text) || !string.IsNullOrWhiteSpace(textBoxRecipient.Text) || !string.IsNullOrWhiteSpace(textBoxMessageID.Text)))
            {
                RetrieveO365SearchResults();
            }
        }
               
        private void checkBoxOffice365_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxOffice365.Checked)
            {
                textBoxUserName.Enabled = true;
                textBoxPassword.Enabled = true;
            }
            else
            {
                textBoxUserName.Enabled = false;
                textBoxPassword.Enabled = false;
            }
        }

        private void exchangeServersCheckedListBox_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if(e.NewValue == CheckState.Checked)
            {
                selectedExchangeServers.Add(exchangeServersCheckedListBox.Items[e.Index].ToString());
            }

            else
            {
                selectedExchangeServers.Remove(exchangeServersCheckedListBox.Items[e.Index].ToString());
            }
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            closeOpenConnections();
        }

        private void textBoxRemotePowerShell_TextChanged(object sender, EventArgs e)
        {
            PowerShellUri = "http://" + textBoxRemotePowerShell.Text + "/PowerShell";
        }

        private void listViewResults_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            // Get the new sorting column.
            ColumnHeader new_sorting_column =  listViewResults.Columns[e.Column];

            // Figure out the new sorting order.
            System.Windows.Forms.SortOrder sort_order;
            if (SortingColumn == null)
            {
                // New column. Sort ascending.
                sort_order = SortOrder.Ascending;
            }
            else
            {
                // See if this is the same column.
                if (new_sorting_column == SortingColumn)
                {
                    // Same column. Switch the sort order.
                    if (SortingColumn.Text.StartsWith("> "))
                    {
                        sort_order = SortOrder.Descending;
                    }
                    else
                    {
                        sort_order = SortOrder.Ascending;
                    }
                }
                else
                {
                    // New column. Sort ascending.
                    sort_order = SortOrder.Ascending;
                }

                // Remove the old sort indicator.
                SortingColumn.Text = SortingColumn.Text.Substring(2);
            }

            // Display the new sort order.
            SortingColumn = new_sorting_column;
            if (sort_order == SortOrder.Ascending)
            {
                SortingColumn.Text = "> " + SortingColumn.Text;
            }
            else
            {
                SortingColumn.Text = "< " + SortingColumn.Text;
            }

            // Create a comparer.
            listViewResults.ListViewItemSorter =
                new ListViewComparer(e.Column, sort_order);

            // Sort.
            listViewResults.Sort();
        }

        private void listViewResults_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ListViewHitTestInfo hit = listViewResults.HitTest(e.Location);

            Clipboard.SetText(hit.SubItem.Text);
        }

        private void labelSender_DoubleClick(object sender, EventArgs e)
        {
            textBoxSender.Text = null;
            textBoxSender.Focus();
        }

        private void labelRecipient_DoubleClick(object sender, EventArgs e)
        {
            textBoxRecipient.Text = null;
            textBoxRecipient.Focus();
        }

        private void labelSubject_DoubleClick(object sender, EventArgs e)
        {
            textBoxSender.Text = null;
            textBoxSender.Focus();
        }

        private void labelMessageID_Click(object sender, EventArgs e)
        {
            textBoxMessageID.Text = null;
            textBoxMessageID.Focus();
        }

        private void buttonGetStatus_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(textBoxActiveSyncUser.Text))
            {
                if (checkBoxOnPrem.Checked)
                {
                    if (!ConnectWithRemotePowerShell())
                        return;

                    PowerShell powershell = PowerShell.Create();
                    PSCommand command = new PSCommand();
                    command.AddCommand("Get-CASMailbox");
                    command.AddParameter("Identity", textBoxActiveSyncUser.Text);

                    powershell.Commands = command;
                    powershell.Runspace = _exchangeRunspace;
                    Collection<object> casMailboxResults = InvokeCommand(powershell);

                    if (casMailboxResults != null)
                    {
                        foreach (PSObject result in casMailboxResults)
                        {
                            labelActiveSyncStatus.Text = result.Properties["ActiveSyncEnabled"].Value.ToString();
                        }
                    }
                    else
                    {
                        labelActiveSyncStatus.Text = "Not Found";
                    }
                }

                if (checkBoxOffice365.Checked)
                {
                    if (!ConnectWithO365PowerShell())
                        return;

                    PowerShell powershell = PowerShell.Create();
                    PSCommand command = new PSCommand();
                    command.AddCommand("Get-CASMailbox");
                    command.AddParameter("Identity", textBoxActiveSyncUser.Text);

                    powershell.Commands = command;
                    powershell.Runspace = _o365Runspace;
                    Collection<object> casMailboxResults = InvokeCommand(powershell);

                    if (casMailboxResults != null)
                    {
                        foreach (PSObject result in casMailboxResults)
                        {
                            labelActiveSyncStatus.Text = result.Properties["ActiveSyncEnabled"].Value.ToString();
                        }
                    }
                    else
                    {
                        labelActiveSyncStatus.Text = "Not Found";
                    }
                }
            }
        }

        private void pictureBox2_MouseClick(object sender, MouseEventArgs e)
        {
            Process.Start("http://www.linkedin.com/in/candede");
        }
    }

    public class ListViewComparer : System.Collections.IComparer
    {
        private int ColumnNumber;
        private SortOrder SortOrder;

        public ListViewComparer(int column_number,
            SortOrder sort_order)
        {
            ColumnNumber = column_number;
            SortOrder = sort_order;
        }

        // Compare two ListViewItems.
        public int Compare(object object_x, object object_y)
        {
            // Get the objects as ListViewItems.
            ListViewItem item_x = object_x as ListViewItem;
            ListViewItem item_y = object_y as ListViewItem;

            // Get the corresponding sub-item values.
            string string_x;
            if (item_x.SubItems.Count <= ColumnNumber)
            {
                string_x = "";
            }
            else
            {
                string_x = item_x.SubItems[ColumnNumber].Text;
            }

            string string_y;
            if (item_y.SubItems.Count <= ColumnNumber)
            {
                string_y = "";
            }
            else
            {
                string_y = item_y.SubItems[ColumnNumber].Text;
            }

            // Compare them.
            int result;
            double double_x, double_y;
            if (double.TryParse(string_x, out double_x) &&
                double.TryParse(string_y, out double_y))
            {
                // Treat as a number.
                result = double_x.CompareTo(double_y);
            }
            else
            {
                DateTime date_x, date_y;
                if (DateTime.TryParse(string_x, out date_x) &&
                    DateTime.TryParse(string_y, out date_y))
                {
                    // Treat as a date.
                    result = date_x.CompareTo(date_y);
                }
                else
                {
                    // Treat as a string.
                    result = string_x.CompareTo(string_y);
                }
            }

            // Return the correct result depending on whether
            // we're sorting ascending or descending.
            if (SortOrder == SortOrder.Ascending)
            {
                return result;
            }
            else
            {
                return -result;
            }
        }
    }
}
