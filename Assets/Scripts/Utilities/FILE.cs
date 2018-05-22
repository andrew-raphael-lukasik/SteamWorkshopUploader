using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;

public static class FILE
{
    
    public static string ReadText ( string filePath )
    {
        string output;
		if( File.Exists( filePath ) )
		{
			StreamReader reader = null;
			try
			{
				using( reader = new StreamReader( filePath , Encoding.UTF8 ) )
				{
					output = reader.ReadToEnd();
				}
			}
			catch ( System.Exception ex )
			{
				Debug.LogException( ex );
				output = null;
			}
			finally
			{
				if( reader!=null )
				{
					reader.Close();
				}
			}
		}
		else
		{
			throw new IOException( string.Format( "File not found: {0}" , filePath ) );
		}
		return output;
    }

    public static Texture2D ReadTexture ( string filePath )
    {
        if( File.Exists( filePath ) )
        {
			try
			{
				Texture2D result = new Texture2D(0,0);
				byte[] bytes = File.ReadAllBytes( filePath );
				if( result.LoadImage( bytes )==false )
				{
					Debug.LogError( string.Format( "Reading preview file failed!: \"{0}\"" , filePath ) );
				}
				return result;
			}
			catch ( System.Exception ex )
			{
				Debug.LogException( ex );
				return null;
			}
		}
        else
        {
            Debug.LogError( string.Format( "Reading preview file failed! Make sure JSON file is filled properly and/or file exists: \"{0}\"", filePath ) );
            return null;
        }
    }

    public static bool WriteText ( string filePath , string text )
    {
        StreamWriter writer = null;
		try
		{
			if( File.Exists( filePath ) )
			{
				using( writer = File.CreateText( filePath )  )
				{
					writer.Write( text );
				}
				return true;
			}
			else
			{
				using( writer = File.CreateText( filePath )  )
				{
					writer.Write( text );
				}
				return true;
			}
		}
		catch ( System.Exception ex )
		{
			Debug.LogException( ex );
			return false;
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