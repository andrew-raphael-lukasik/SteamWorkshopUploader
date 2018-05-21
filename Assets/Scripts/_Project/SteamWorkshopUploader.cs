using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.Assertions;

using Steamworks;

namespace Project
{
    [DisallowMultipleComponent]
    public class SteamWorkshopUploader : MonoBehaviour
    {
        #region FIELDS

        public const int version = 6;

        public Text versionText;
        public Text statusText;
        public Slider progressBar;

        public RectTransform packListRoot;
        public GameObject packListButtonPrefab;

        [Header("ModPack Interface")]
        public RectTransform currentItemPanel;
        public Text submitButtonText;
        public Text modPackContents;
        public RawImage modPackPreview;
        public InputField modPackName;
        public InputField modPackTitle;
        public InputField modPackPreviewFilename;
        public InputField modPackContentFolder;
        public InputField modPackChangeNotes;
        public InputField modPackDescription;
        public InputField modPackTags;
        public Dropdown modPackVisibility;

        private const string defaultFilename = "MyNewMod.workshop.json";
        private const string defaultFolderName = "MyNewMod";
        private const string relativeBasePath = "/../WorkshopContent/";
        private string basePath;

        private WorkshopModPack currentPack;
        private string currentPackFilename;
        private UGCUpdateHandle_t currentHandle = UGCUpdateHandle_t.Invalid;

        protected CallResult<CreateItemResult_t> m_itemCreated;
        protected CallResult<SubmitItemUpdateResult_t> m_itemSubmitted;

        #endregion
        #region MONOBEHAVIOUR METHODS

        void Awake ()
        {
            SetupDirectories();
            
            //make sure at least default steam_appid file exists:
            {
                string appidFilePath = Application.dataPath + "/../steam_appid.txt";
                if( File.Exists( appidFilePath )==false )
                {
                    StreamWriter writer = null;
                    try
                    {
                        using( writer = File.CreateText( appidFilePath )  )
                        {
                            writer.Write( 480 );
                        }
                    }
                    catch ( System.Exception ex )
                    {
                        Debug.LogException( ex );
                    }
                    finally
                    {
                        if( writer!=null )
                        {
                            writer.Close();
                        }
                    }
                }
            }

        }

        void Start ()
        {
            versionText.text = string.Format( "Steam Workshop Uploader - Build {0} --- App ID: {1}" , version , SteamUtils.GetAppID() );

            if( SteamUtils.GetAppID()==AppId_t.Invalid )
            {
                Debug.LogError( "ERROR: Steam App ID isn't set! Make sure 'steam_appid.txt' is placed next to the executable file, and contains a single line with the app id." , this );
            } 

            RefreshPackList();
            RefreshCurrentModPack();
        }

        void OnEnable ()
        {
            if( SteamManager.Initialized )
            {
                m_NumberOfCurrentPlayers = CallResult<NumberOfCurrentPlayers_t>.Create(OnNumberOfCurrentPlayers);

                m_itemCreated = CallResult<CreateItemResult_t>.Create(OnItemCreated);
                m_itemSubmitted = CallResult<SubmitItemUpdateResult_t>.Create(OnItemSubmitted);
            }
        }

        void Update ()
        {
            //?
            if( Input.GetKeyDown(KeyCode.F1) )
            {
                SteamAPICall_t handle = SteamUserStats.GetNumberOfCurrentPlayers();
                m_NumberOfCurrentPlayers.Set(handle);
                Debug.Log("Called GetNumberOfCurrentPlayers()");
            }

            //refresh progres bar:
            if( currentHandle!=UGCUpdateHandle_t.Invalid )
            {
                UpdateProgressBar( currentHandle );
            }
            else
            {
                progressBar.value = 0f;
            }
            
            //help unity enforce 4:3 aspect ratio
            {
                #if UNITY_STANDALONE
                if( (Screen.width/Screen.height) != 4f/3f )
                {
                    Screen.SetResolution( Screen.width , (int)(Screen.width * 3f/4f) , false , 10 );
                }
                #endif
            }
        }

        void OnApplicationQuit ()
        {
            if( currentPack!=null )
            {
                OnCurrentModPackChanges();
                SaveCurrentModPack();
            }
            SteamAPI.Shutdown();
        }

        void OnApplicationFocus ()
        {
            RefreshPackList();

            if( currentPack!=null )
            {
                RefreshCurrentModPack();
            }
        }

        #endregion
        #region PUBLIC METHODS        

        public void Shutdown()
        {
            SteamAPI.Shutdown();
        }

        void SetupDirectories ()
        {
            basePath = Application.dataPath + relativeBasePath;

            #if DEBUG
            //Debug.Log( string.Format( " Application.dataPath: {0}" ,  Application.dataPath ) );
            //Debug.Log( string.Format( " relativeBasePath: {0}" ,  relativeBasePath ) );
            Assert.IsTrue( basePath.Length>1 && basePath[1]==':' );
            #endif

            if( !Directory.Exists( basePath ) )
            {
                Directory.CreateDirectory( basePath );
            }

            #if DEBUG
            Debug.Log( string.Format( "basePath is: {0}" , basePath ) );
            #endif
        }

        public string[] GetPackFilenames ()
        {
            return Directory.GetFiles( basePath , "*.workshop.json" , SearchOption.TopDirectoryOnly );
        }

        public void ClearPackList ()
        {
            foreach( Transform child in packListRoot )
            {
                Destroy( child.gameObject );
            }
        }

        public void RefreshPackList ()
        {
            ClearPackList();

            var paths = GetPackFilenames();

            // create list of buttons using prefabs
            // hook up their click events to the right function
            for( int i=0 ; i<paths.Length ; i++ )
            {
                string packPath = paths[i];
                string packName = Path.GetFileName( packPath );

                var buttonObj = Instantiate( packListButtonPrefab , Vector3.zero , Quaternion.identity );
                var button = buttonObj.GetComponent<Button>();
                button.transform.SetParent( packListRoot );

                button.GetComponentInChildren<Text>().text = packName.Replace( ".workshop.json" , string.Empty );
                
                if( button!=null )
                {
                    string fileName = packPath;
                    button.onClick.AddListener(
                        () => { SelectModPack( fileName ); }
                    );
                }
            }
        }

        public void RefreshCurrentModPack()
        {
            if( currentPack==null )
            {
                currentItemPanel.gameObject.SetActive(false);
                return;
            }

            currentItemPanel.gameObject.SetActive(true);

            var filename = currentPack.filename;

            submitButtonText.text = string.Format(
                "Submit {0}" ,
                Path.GetFileNameWithoutExtension( filename.Replace( ".workshop" , string.Empty ) )
            );
            modPackContents.text = JsonUtility.ToJson( currentPack , true );

            RefreshPreview();

            modPackTitle.text = currentPack.title;
            modPackPreviewFilename.text = currentPack.previewfile;
            modPackContentFolder.text = currentPack.contentfolder;
            modPackDescription.text = currentPack.description;
            modPackTags.text = string.Join( "," , currentPack.tags.ToArray() );
            modPackVisibility.value = currentPack.visibility;
        }
        
        public void SelectModPack ( string filename )
        {
            if( currentPack!=null )
            {
                OnCurrentModPackChanges();
                SaveCurrentModPack();
            }

            var pack = WorkshopModPack.Load( filename );

            if( pack!=null )
            {
                currentPack = pack;
                currentPackFilename = filename;

                RefreshCurrentModPack();
                //EditModPack(filename);
            }
        }

        public void EditModPack ( string packPath )
        {
            System.Diagnostics.Process.Start( packPath );
        }

        public void RefreshPreview ()
        {
            //create full file path
            string filePath = basePath + currentPack.previewfile;

            //test assertions:
            if( currentPack.previewfile==null )
            {
                Debug.LogError( $"{ nameof(currentPack) }.{ nameof(currentPack.previewfile) } is null" );
                modPackPreview.texture = null;
                return;
            }
            else if( currentPack.previewfile.Length==0 )
            {
                Debug.LogError( $"pack { nameof(currentPack.previewfile) } is empty" );
                modPackPreview.texture = null;
                return;
            }
            
            //if there is no preview image then copy default one here:
            if( File.Exists( filePath )==false )
            {
                File.Copy(
                    Application.streamingAssetsPath + "/templates/512px-512px.png" ,
                    filePath,
                    false
                );
            }

            //read texture file:
            modPackPreview.texture = FILE.ReadTexture2D( filePath );
        }

        public bool ValidateModPack ( WorkshopModPack pack )
        {
            statusText.text = "Validating mod pack...";

            string path = basePath + pack.previewfile;

            var info = new FileInfo( path );
            if( info.Length >= 1024 * 1024 )
            {
                statusText.text = "ERROR: Preview file must be <1MB!";
                return false;
            }

            return true;
        }

        public void OnCurrentModPackChanges ()
        {
            OnChanges( currentPack );
            RefreshCurrentModPack();
        }

        public void OnChanges( WorkshopModPack pack )
        {
            // interface stuff
            pack.previewfile = modPackPreviewFilename.text;
            pack.title = modPackTitle.text;
            pack.description = modPackDescription.text;
            pack.tags = new List<string>( modPackTags.text.Split(',') );
            pack.visibility = modPackVisibility.value;
        }

        public void AddModPack ()
        {
            var packName = modPackName.text;

            // validate modpack name
            if( string.IsNullOrEmpty( packName ) || packName.Contains(".") )
            {
                statusText.text = string.Format( "Bad modpack name: {0}" , modPackName.text );
            }
            else
            {
                string packJsonFilePath = string.Format(
                    "{0}.workshop.json",
                    basePath + packName
                );

                var pack = new WorkshopModPack();
                pack.Save( packJsonFilePath );

                //determine paths:
                var contentRelPath = modPackName.text;
                var contentAbsPath = basePath + contentRelPath;

                //
                pack.contentfolder = contentRelPath;

                //create content directory:
                Directory.CreateDirectory( contentAbsPath );

                //create first file:
                File.Copy(
                    Application.streamingAssetsPath + "/templates/hello_steam.txt",
                    contentAbsPath + "/hello_steam.txt",
                    false
                );
                
                RefreshPackList();

                SelectModPack( packJsonFilePath );

                CreateWorkshopItem();
            }
        }
        
        public void SaveCurrentModPack ()
        {
            if( currentPack!=null && !string.IsNullOrEmpty( currentPackFilename ) )
            {
                currentPack.Save(currentPackFilename);
            }
        }

        public void SubmitCurrentModPack ()
        {
            if (currentPack != null)
            {
                OnChanges(currentPack);
                SaveCurrentModPack();
                
                if (ValidateModPack(currentPack))
                {
                    UploadModPack(currentPack);
                }
            }
        }

        #endregion
        #region PRIVATE METHODS

        void CreateWorkshopItem ()
        {
            if( string.IsNullOrEmpty( currentPack.publishedfileid ) )
            {
                SteamAPICall_t call = SteamUGC.CreateItem(
                     SteamUtils.GetAppID(),
                    Steamworks.EWorkshopFileType.k_EWorkshopFileTypeCommunity
                );
                m_itemCreated.Set(call);

                statusText.text = "Creating new item...";
            }
        }

        void UploadModPack ( WorkshopModPack pack )
        {
            ulong ulongId = ulong.Parse( pack.publishedfileid );
            var id = new PublishedFileId_t(ulongId);

            UGCUpdateHandle_t handle = SteamUGC.StartItemUpdate(  SteamUtils.GetAppID() , id );
            //m_itemUpdated.Set(call);
            //OnItemUpdated(call, false);

            // Only set the changenotes when clicking submit
            pack.changenote = modPackChangeNotes.text;

            currentHandle = handle;
            SetupModPack( handle , pack );
            SubmitModPack( handle , pack );
        }

        void SetupModPack ( UGCUpdateHandle_t handle , WorkshopModPack pack )
        {
            SteamUGC.SetItemTitle(
                handle,
                pack.title
            );
            SteamUGC.SetItemDescription(
                handle,
                pack.description
            );
            SteamUGC.SetItemVisibility(
                handle,
                (ERemoteStoragePublishedFileVisibility)pack.visibility
            );
            SteamUGC.SetItemContent(
                handle,
                basePath + pack.contentfolder
            );
            SteamUGC.SetItemPreview(
                handle,
                basePath + pack.previewfile
            );
            SteamUGC.SetItemMetadata(
                handle,
                pack.metadata
            );
            SteamUGC.SetItemTags(
                handle,
                pack.tags
            );
        }

        void SubmitModPack( UGCUpdateHandle_t handle , WorkshopModPack pack )
        {
            SteamAPICall_t call = SteamUGC.SubmitItemUpdate( handle , pack.changenote );
            m_itemSubmitted.Set( call );
            //In the same way as Creating a Workshop Item, confirm the user has accepted the legal agreement. This is necessary in case where the user didn't initially create the item but is editing an existing item.
        }

        void OnItemCreated(CreateItemResult_t callback, bool ioFailure)
        {
            if (ioFailure)
            {
                statusText.text = "Error: I/O Failure! :(";
                return;
            }

            switch(callback.m_eResult)
            {
                case EResult.k_EResultInsufficientPrivilege:
                    // you're banned!
                    statusText.text = "Error: Unfortunately, you're banned by the community from uploading to the workshop! Bummer. :(";
                    break;
                case EResult.k_EResultTimeout:
                    statusText.text = "Error: Timeout :S";
                    break;
                case EResult.k_EResultNotLoggedOn:
                    statusText.text = "Error: You're not logged into Steam!";
                    break;
            }

            if(callback.m_bUserNeedsToAcceptWorkshopLegalAgreement)
            {
                /*
                * Include text next to the button that submits an item to the workshop, something to the effect of: “By submitting this item, you agree to the workshop terms of service” (including the link)
    After a user submits an item, open a browser window to the Steam Workshop page for that item by calling:
    SteamFriends()->ActivateGameOverlayToWebPage( const char *pchURL );
    pchURL should be set to steam://url/CommunityFilePage/PublishedFileId_t replacing PublishedFileId_t with the workshop item Id.
    This has the benefit of directing the author to the workshop page so that they can see the item and configure it further if necessary and will make it easy for the user to read and accept the Steam Workshop Legal Agreement.
                * */
            }

            if(callback.m_eResult == EResult.k_EResultOK)
            {
                statusText.text = "Item creation successful! Published Item ID: " + callback.m_nPublishedFileId.ToString();
                Debug.Log("Item created: Id: " + callback.m_nPublishedFileId.ToString());

                currentPack.publishedfileid = callback.m_nPublishedFileId.ToString();
                /*
                string filename = basePath + modPackName.text + ".workshop.json";

                var pack = new WorkshopModPack();
                pack.publishedfileid = callback.m_nPublishedFileId.ToString();
                pack.Save(filename);

                Directory.CreateDirectory(basePath + modPackName.text);
                
                RefreshPackList();
                */
            }
        }

        void OnItemSubmitted(SubmitItemUpdateResult_t callback, bool ioFailure)
        {
            if (ioFailure)
            {
                statusText.text = "Error: I/O Failure! :(";
                return;
            }

            switch(callback.m_eResult)
            {
                case EResult.k_EResultOK:
                    statusText.text = "SUCCESS! Item submitted! :D :D :D";
                    currentHandle = UGCUpdateHandle_t.Invalid;
                    break;
            }
        }

        void UpdateProgressBar ( UGCUpdateHandle_t handle )
        {
            ulong bytesDone;
            ulong bytesTotal;
            EItemUpdateStatus status = SteamUGC.GetItemUpdateProgress( handle , out bytesDone , out bytesTotal );

            float progress = (float)bytesDone / (float)bytesTotal;
            progressBar.value = progress;

            switch( status )
            {
                case EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges:
                    statusText.text = "Committing changes...";
                    break;
                case EItemUpdateStatus.k_EItemUpdateStatusInvalid:
                    {
                        int numContentFiles = System.IO.Directory.GetFiles( currentPack.filename ).Length;
                        if( numContentFiles==0 )
                        {
                            statusText.text = "Item invalid. Make sure your mod contains at leats one file.";
                        } 
                        else
                        {
                            statusText.text = "Item invalid. No idea why. Google \"steam workshop k_EItemUpdateStatusInvalid\"";
                        }
                    }
                    break;
                case EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile:
                    statusText.text = "Uploading preview image...";
                    break;
                case EItemUpdateStatus.k_EItemUpdateStatusUploadingContent:
                    statusText.text = "Uploading content...";
                    break;
                case EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig:
                    statusText.text = "Preparing configuration...";
                    break;
                case EItemUpdateStatus.k_EItemUpdateStatusPreparingContent:
                    statusText.text = "Preparing content...";
                    break;
            }

        }


        CallResult<NumberOfCurrentPlayers_t> m_NumberOfCurrentPlayers;

        void OnNumberOfCurrentPlayers(NumberOfCurrentPlayers_t pCallback, bool bIOFailure)
        {
            if (pCallback.m_bSuccess != 1 || bIOFailure)
            {
                Debug.Log("There was an error retrieving the NumberOfCurrentPlayers.");
            }
            else
            {
                Debug.Log("The number of players playing your game: " + pCallback.m_cPlayers);
            }
        }

        #endregion
        #region NESTED TYPES

        [System.Serializable]
        public class WorkshopModPack
        {
            // gets populated when the modpack is loaded; shouldn't be serialized since it would go out of sync
            [System.NonSerialized] public string filename;

            // populated by the app, should generally be different each time anyways
            [System.NonSerialized] public string changenote = "Version 1.0";
            
            // string, because this is a ulong and JSON doesn't like em
            public string publishedfileid = "";
            public string contentfolder = "";
            public string previewfile = "";
            public int visibility = 0;//first upload is always hidden
            public string title = "My New Mod Pack";
            public string description = "Description goes here";
            public string metadata = "";
            public List<string> tags = new List<string>();

            public static WorkshopModPack Load( string filePath )
            {
                string json = FILE.ReadText( filePath );
                WorkshopModPack pack = JsonUtility.FromJson<WorkshopModPack>( json );
                pack.filename = filePath;
                return pack;
            }

            public void Save ( string filePath )
            {
                string json = JsonUtility.ToJson( this , true );
                FILE.WriteText( filePath , json );

                #if DEBUG
                Debug.Log( string.Format( "Saved modpack to file: {0}" , filePath ) );
                #endif
            }
            
        }

        #endregion
    }
    
}