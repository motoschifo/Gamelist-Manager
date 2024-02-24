﻿using GamelistManager.control;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GamelistManager
{
    public partial class ScraperForm : Form
    {
        private GamelistManagerForm gamelistManagerForm;
        private ScraperPreview scraperPreview;
        private CancellationTokenSource CancellationTokenSource;
        public CancellationToken CancellationToken => CancellationTokenSource.Token;
        private static Stopwatch globalStopwatch = new Stopwatch();
        private int scraperCount;
        private bool scraperFinished;
        private int totalCount;
        private int scrapeErrors;
        private bool hideNonGame;
        private bool noZZZ;
        private bool scrapeByGameID;
        private string scraperName;

        public ScraperForm(GamelistManagerForm form)
        {
            InitializeComponent();
            scraperPreview = new ScraperPreview();
            CancellationTokenSource = new CancellationTokenSource();
            scraperCount = 0;
            totalCount = 0;
            scrapeErrors = 0;
            scraperFinished = false;
            gamelistManagerForm = form;
            noZZZ = false;
            hideNonGame = false;
            scrapeByGameID = false;
            scraperName = null;
        }

        private List<string> GetElementsToScrape()
        {
            List<string> elements = new List<string>();
            foreach (Control control in panelCheckboxes.Controls)
            {
                if (control is CheckBox checkBox && checkBox.Checked && checkBox.Enabled)
                {
                    string elementName = checkBox.Name.Replace("checkbox", "").ToLower();
                    elements.Add(elementName);
                }
            }
            return elements;
        }

        private List<(string, string, int)> GetRomsToScrape()
        {
            // Make a list of romNames to scrape
            List<(string, string, int)> romNames = null;
            List<DataGridViewRow> rows = new List<DataGridViewRow>();

            bool excludeHidden = checkBoxDoNotScrapeHidden.Checked;

            if (radioButtonScrapeAll.Checked)
            {
                // Include both selected and unselected rows
                rows = gamelistManagerForm.DataGridView.Rows.Cast<DataGridViewRow>().ToList();
            }
            else
            {
                // Only include selected rows
                rows = gamelistManagerForm.DataGridView.SelectedRows.Cast<DataGridViewRow>().ToList();
            }

            romNames = rows
           .Where(row => !excludeHidden || !(row.Cells["hidden"].Value is bool hidden && hidden))
            .Select(row =>
             {
                 // Extract value from each row
                 string cellPath = row.Cells["path"].Value.ToString().TrimStart('.', '/');
                 string cellName = row.Cells["name"].Value.ToString();

                 int gameID = 0;
                 if (row.Cells["id"].Value != null)
                 {
                     int.TryParse(row.Cells["id"].Value.ToString(), out gameID);
                 }
                 return (cellPath, cellName, gameID);
             })
            .ToList();
            return romNames;
        }

        private async void StartScraping()
        {
            gamelistManagerForm.Enabled = false;
            
            // List creation
            List<string> elements = GetElementsToScrape();

            List<(string, string, int)> romPaths = GetRomsToScrape();

            if (elements.Count == 0)
            {
                MessageBox.Show("No metadata selection was made", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            // Clear Log
            if (listBoxLog.Items.Count > 0)
            {
                listBoxLog.Items.Clear();
            }

            // Form settings
            panelScraperOptions.Enabled = false;
            buttonStart.Enabled = false;
            buttonCancel.Enabled = true;
            buttonSetup.Enabled = false;
            globalStopwatch.Reset();
            globalStopwatch.Start();
            panelCheckboxes.Visible = false;
            panelSmall.Controls.Add(scraperPreview);

            // Set Variables
            scraperCount = 0;
            totalCount = romPaths.Count;
            scrapeErrors = 0;
            bool overwrite = checkBoxOverwriteExisting.Checked;
            scraperFinished = false;
            string parentFolderPath = Path.GetDirectoryName(SharedData.XMLFilename);
            string parentFolderName = Path.GetFileName(parentFolderPath);

            // Reset progressbar
            progressBarScrapeProgress.Value = 0;
            progressBarScrapeProgress.Minimum = 0;
            progressBarScrapeProgress.Step = 1;
            progressBarScrapeProgress.Maximum = totalCount;

            // Reset cancellation tokensource
            CancellationTokenSource = new CancellationTokenSource();

            // Add a deserialized scraper column if it does not exist
            scraperName = comboBoxScrapers.Text;
            if (!SharedData.DataSet.Tables[0].Columns.Contains($"scrap_{scraperName}"))
            {
                SharedData.DataSet.Tables[0].Columns.Add($"scrap_{scraperName}");
            }

            switch (comboBoxScrapers.SelectedIndex)
            {
                case 0:

                    if (parentFolderName != "mame" && parentFolderName != "fbneo")
                    {
                        MessageBox.Show("This doesn't appear to be a gamelist for Mame or FBNeo!\n" +
                       "You cannot scrape this gamelist with ArcadeDB.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        try
                        {
                            scraperFinished = await ArcadeDBAsync(parentFolderPath, elements, romPaths, overwrite);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"An error has occured!\n{ex.Message}", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    break;

                case 1:

                    // Always scrape id
                    elements.Add("id");

                    // Get the system Id
                    SystemIdResolver resolver = new SystemIdResolver();
                    int systemId = resolver.ResolveSystemId(parentFolderName);
                    if (systemId == 0)
                    {
                        MessageBox.Show("The system could not be found!", "Missing System", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        // Get ScreenSraper creds
                        (string userId, string userPassword) = CredentialManager.GetCredentials("ScreenScraper");
                        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userPassword))
                        {
                            MessageBox.Show("ScreenScraper credentials are not set!", "Missing Credentials", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        else
                        {
                            try
                            {
                                scraperFinished = await ScreenScraperAsync(parentFolderPath, systemId.ToString(), userId, userPassword, elements, romPaths, overwrite);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"An error has occurred!\n{ex.Message}", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                    break;
            }

            if (scraperFinished)
            {
                AddToLog("Scraping completed!");
            }
            else
            {
                AddToLog("Scraping cancelled!");
            }

            SharedData.DataSet.AcceptChanges();

            if (!checkBoxSupressNotify.Checked)
            {
                if (scraperCount > 0)
                {
                    string elapsedTime = $"{globalStopwatch.Elapsed.TotalMinutes:F0} minutes and {globalStopwatch.Elapsed.Seconds} seconds";
                    MessageBox.Show($"Finished scraping {scraperCount} items in {elapsedTime}");

                    if (checkBoxSave.Checked)
                    {
                        SaveReminder();
                    }
                }
            }

            panelScraperOptions.Enabled = true;
            buttonStart.Enabled = true;
            buttonCancel.Enabled = false;
            buttonSetup.Enabled = true;
            globalStopwatch.Stop();
            labelProgress.Text = "0%";
            panelCheckboxes.Visible = true;
            panelSmall.Controls.Remove(scraperPreview);

            gamelistManagerForm.Enabled = true;

        }

        private void SaveReminder()
        {
            gamelistManagerForm.SaveFile(SharedData.XMLFilename);
        }

        public async Task<bool> ArcadeDBAsync(
        string folderPath,
        List<string> elementList,
        List<(string, string, int)> romList,
        bool overwrite
        )
        {
            ScrapeArcadeDB scrapeArcadeDB = new ScrapeArcadeDB();
            try
            {
                foreach ((string romName, string metadataName, int gameID) in romList)
                {
                    CancellationToken.ThrowIfCancellationRequested();
                    AddToLog($"Scraping rom '{romName}'");
                    Interlocked.Increment(ref scraperCount);
                    UpdateLabel(scraperCount, totalCount, scrapeErrors);
                    UpdateProgressBar();

                    Dictionary<string, string> result = await scrapeArcadeDB.ScrapeArcadeDBAsync(
                       romName,
                       folderPath,
                       overwrite,
                       elementList
                   );
                    ScraperCommon(overwrite, folderPath, romName, result);
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation
                return false; // Scraper was cancelled
            }
            return true;
        }

        public async Task<bool> ScreenScraperAsync(
        string folderPath,
        string systemId,
        string userId,
        string userPassword,
        List<string> elementList,
        List<(string, string, int)> romList,
        bool overwrite
        )
        {
            string boxSource = RegistryManager.ReadRegistryValue("BoxSource");
            boxSource = boxSource ?? string.Empty;
            string imageSource = RegistryManager.ReadRegistryValue("ImageSource");
            imageSource = imageSource ?? string.Empty;
            string logoSource = RegistryManager.ReadRegistryValue("LogoSource");
            logoSource = logoSource ?? string.Empty;
            string region = RegistryManager.ReadRegistryValue("Region");
            region = region ?? "us";
            string language = RegistryManager.ReadRegistryValue("language");
            language = language ?? "en";
            bool.TryParse(RegistryManager.ReadRegistryValue("HideNonGame"), out hideNonGame);
            bool.TryParse(RegistryManager.ReadRegistryValue("NoZZZ"), out noZZZ);
            bool.TryParse(RegistryManager.ReadRegistryValue("ScrapeByGameID"), out scrapeByGameID);

            string devId = "";
            string devPassword = "";

            // Set the maximum number of concurrent tasks
            string maxThreadsValue = RegistryManager.ReadRegistryValue("MaxThreads");
            int maxC = 1;
            if (!string.IsNullOrEmpty(maxThreadsValue))
            {
                maxC = int.Parse(maxThreadsValue);
            }

            int maxConcurrency = maxC; // Adjust the number as needed
            SemaphoreSlim semaphoreSlim = new SemaphoreSlim(maxConcurrency);

            // Create a list to store the tasks
            List<Task> tasks = new List<Task>();

            try
            {

                foreach ((string romName, string metadataName, int gameID) in romList)
                {
                    // Scrape by game id or not
                    int id = gameID;
                    if (scrapeByGameID == false || gameID < 1)
                    {
                        id = 0;
                    }

                    string showId = (id != 0) ? $"[{id}]" : string.Empty;

                    // Check cancelation token
                    await semaphoreSlim.WaitAsync(); // Wait until there's room to proceed
                    CancellationToken.ThrowIfCancellationRequested();

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            AddToLog($"Scraping rom {showId} '{romName}'");
                            Interlocked.Increment(ref scraperCount);
                            ScrapeScreenScraper scraper = new ScrapeScreenScraper();
                            Dictionary<string, string> result;
                            result = await scraper.ScrapeScreenScraperAsync(
                               userId,
                               userPassword,
                               devId,
                               devPassword,
                               region,
                               language,
                               romName,
                               systemId,
                               folderPath,
                               overwrite,
                               elementList,
                               boxSource,
                               imageSource,
                               logoSource,
                               id
                           );
                            if (result == null && romName != metadataName)
                            {
                                // try to scrape by meta name if it is different
                                AddToLog($"Trying by name '{metadataName}'");
                                result = await scraper.ScrapeScreenScraperAsync(
                                userId,
                                userPassword,
                                devId,
                                devPassword,
                                region,
                                language,
                                metadataName,
                                systemId,
                                folderPath,
                                overwrite,
                                elementList,
                                boxSource,
                                imageSource,
                                logoSource,
                                id
                            );
                            }
                            ScraperCommon(overwrite, folderPath, romName, result);
                        }
                        finally
                        {
                            UpdateProgressBar();
                            UpdateLabel(scraperCount, totalCount, scrapeErrors);
                            semaphoreSlim.Release(); // Release the semaphore after the task is complete
                        }
                    }
                    ));
                }

                // Wait for all tasks to complete
                await Task.WhenAll(tasks);
                return true; // Scraper finished successfully
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation
                return false; // Scraper was cancelled
            }
        }

        private void ScraperCommon(bool overwrite, string folderPath, string romName, Dictionary<string, string> result)
        {
            if (result == null)
            {
                // Handle error, if needed
                AddToLog($"Unable to scrape '{romName}'");
                Interlocked.Increment(ref scrapeErrors);
                UpdateLabel(scraperCount, totalCount, scrapeErrors);
                return;
            }

            string marquee = null;
            if (result.ContainsKey("marquee"))
            {
                marquee = result["marquee"];
            }

            string image = null;
            if (result.ContainsKey("image"))
            {
                image = result["image"];
            }

            if (!string.IsNullOrEmpty(marquee) && !string.IsNullOrEmpty(image))
            {
                string imageFileName1 = Path.GetFileName(marquee);
                string imageFileName2 = Path.GetFileName(image);
                string imagePath1 = $"{folderPath}\\images\\{imageFileName1}";
                string imagePath2 = $"{folderPath}\\images\\{imageFileName2}";
                this.Invoke((MethodInvoker)delegate
                {
                    // Assuming this control has access to UpdatePictureBox method
                    scraperPreview.UpdatePictureBox(imagePath1, imagePath2);
                });
            }

            lock (SharedData.DataLock)
            {
                DataRow tableRow = SharedData.DataSet.Tables[0].AsEnumerable()
                 .FirstOrDefault(r => r.Field<string>("path") == $"./{romName}");
                // Process scraped items
                foreach (var scrapedItem in result)
                {
                    string elementName = scrapedItem.Key;
                    string elementValue = scrapedItem.Value;
                    if (string.IsNullOrEmpty(elementValue))
                    {
                        continue;
                    }

                    var cellValue = tableRow[elementName];
                    if (overwrite || cellValue == null || cellValue == DBNull.Value || string.IsNullOrEmpty(cellValue.ToString()))
                    {

                        if (elementName == "name" && elementValue.Contains("notgame"))
                        {
                            if (hideNonGame)
                            {
                                tableRow["hidden"] = true;
                            }
                            if (noZZZ)
                            {
                                elementValue = (elementValue.Replace("ZZZ(notgame)", "")).Trim();
                            }
                        }
                        tableRow[elementName] = elementValue;
                    }
                }

                // Scraper information is deserialized in the dataset
                // It is put back in order when saved
                string now = DateTime.Now.ToString();
                string iso8601Format = ISO8601Converter.ConvertToISO8601(now);
                tableRow[$"scrap_{scraperName}"] = iso8601Format;

            }
        }
        private void ButtonSelectAll_Click(object sender, EventArgs e)
        {
            foreach (Control control in panelCheckboxes.Controls)
            {
                // Check if the control is a checkbox
                if (control is System.Windows.Forms.CheckBox checkBox && checkBox.Enabled == true)
                {
                    // Set checked
                    checkBox.Checked = true;
                }
            }
        }

        private void ButtonSelectNone_Click(object sender, EventArgs e)
        {
            foreach (Control control in panelCheckboxes.Controls)
            {
                // Check if the control is a checkbox
                if (control is System.Windows.Forms.CheckBox checkBox && checkBox.Enabled == true)
                {
                    // Set unchecked
                    checkBox.Checked = false;
                }
            }
        }

        private void Scraper_Load(object sender, EventArgs e)
        {
            comboBoxScrapers.SelectedIndex = 1;

            (string userName, string userPassword) = CredentialManager.GetCredentials("ScreenScraper");
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(userPassword))
            {
                buttonStart.Enabled = false;
            }

            string romPath = Path.GetFileName(Path.GetDirectoryName(SharedData.XMLFilename));
            Image image = (Bitmap)Properties.Resources.ResourceManager.GetObject(romPath);

            if (image is Image)
            {
                pictureBox1.Image = image;
            }
        }

        public void AddToLog(string logMessage)
        {
            Action updateListBox = () =>
            {
                listBoxLog.Items.Add($"{logMessage}");
                listBoxLog.TopIndex = listBoxLog.Items.Count - 1;
            };

            if (listBoxLog.InvokeRequired)
            {
                listBoxLog.Invoke(updateListBox);
            }
            else
            {
                updateListBox();
            }
        }

        private DateTime startTime = DateTime.Now;

        public void UpdateLabel(int current, int total, int errors)
        {
            if (labelProgress == null || labelCounts == null) return;

            if (labelProgress.InvokeRequired || labelCounts.InvokeRequired)
            {
                labelProgress.Invoke(new Action(() => UpdateLabel(current, total, errors)));
            }
            else
            {
                double progress = (double)current / total * 100;

                TimeSpan elapsed = DateTime.Now - startTime;

                double remainingMilliseconds = (elapsed.TotalMilliseconds / progress) * (100 - progress);
                TimeSpan remainingTime = TimeSpan.FromMilliseconds(remainingMilliseconds);

                // Display only non-zero components
                string remainingTimeString = string.Empty;

                if (remainingTime.Hours > 0)
                {
                    remainingTimeString += $"{remainingTime.Hours:D2}h ";
                }

                if (remainingTime.Minutes > 0)
                {
                    remainingTimeString += $"{remainingTime.Minutes:D2}m ";
                }

                remainingTimeString += $"{remainingTime.Seconds:D2}s";

                labelProgress.Text = $"{progress:F0}% | Remaining Time: {remainingTimeString}";

                // Update the labelCounts with the current count and totalCount count
                labelCounts.Text = $"Count:{current}/{total} | Errors:{errors}";
            }
        }

        public void UpdateProgressBar()
        {
            if (progressBarScrapeProgress.InvokeRequired)
            {
                progressBarScrapeProgress.Invoke(new Action(() => progressBarScrapeProgress.Value++));
            }
            else
            {
                progressBarScrapeProgress.Value++;
            }
        }


        private void ComboBox_SelectScraper_SelectedIndexChanged(object sender, EventArgs e)
        {
            List<string> availableScraperElements = new List<string>();
            if (comboBoxScrapers.SelectedIndex == 0)
            {
                // ArcadeDB
                buttonSetup.Enabled = false;
                availableScraperElements = new List<string>{
                    "name",
                    "desc",
                    "genre",
                    "players",
                    "rating",
                    "lang",
                    "releasedate",
                    "publisher",
                    "marquee",
                    "image",
                    "video"
                };
            }

            if (comboBoxScrapers.SelectedIndex == 1)
            {
                // ScreenScraper
                buttonSetup.Enabled = true;
                availableScraperElements = new List<string>{
                    "name",
                    "desc",
                    "genre",
                    "players",
                    "rating",
                    "lang",
                    "releasedate",
                    "publisher",
                    "marquee",
                    "image",
                    "video",
                    "developer",
                    "map",
                    "region",
                    "manual",
                    "bezel",
                    "thumbnail",
                    "boxback",
                    "fanart",
                    "arcadesystemname",
                    "genreid"
                };
            }

            foreach (Control control in panelCheckboxes.Controls)
            {
                if (control is CheckBox checkBox)
                {
                    string checkboxShortName = control.Name.Replace("checkbox", "").ToLower();
                    if (availableScraperElements.Contains(checkboxShortName))
                    {
                        checkBox.Enabled = true;
                    }

                    else
                    {
                        checkBox.Enabled = false;
                    }
                }
            }
        }

        private void Button_Stop_Click(object sender, EventArgs e)
        {
            CancellationTokenSource.Cancel();
            buttonCancel.Enabled = false;
            AddToLog("Cancelling scraper task......");
            globalStopwatch.Stop();
            labelProgress.Text = "0%";
        }

        private void ButtonSetup_Click(object sender, EventArgs e)
        {
            ScreenScraperSetup screenScraperSetup = new ScreenScraperSetup
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(this.Location.X + 50, this.Location.Y + 50)
            };
            screenScraperSetup.ShowDialog();
        }

        private void ButtonStartStop_Click(object sender, EventArgs e)
        {
            StartScraping();
            SharedData.IsDataChanged = true;
        }


    }

}


