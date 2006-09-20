/* 
 *	Copyright (C) 2005-2006 Team MediaPortal
 *	http://www.team-mediaportal.com
 *
 *  This Program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2, or (at your option)
 *  any later version.
 *   
 *  This Program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 *  GNU General Public License for more details.
 *   
 *  You should have received a copy of the GNU General Public License
 *  along with GNU Make; see the file COPYING.  If not, write to
 *  the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA. 
 *  http://www.gnu.org/copyleft/gpl.html
 *
 */
using System;
using System.Collections;
using System.Globalization;
using SQLite.NET;
using MediaPortal.Util;
using MediaPortal.GUI.Library;
using MediaPortal.GUI.Pictures;
using MediaPortal.Database;

namespace MediaPortal.Picture.Database
{
  /// <summary>
  /// Summary description for Class1.
  /// </summary>
  public class PictureDatabase : IPictureDatabase, IDisposable
  {
    IPictureDatabase _database = DatabaseFactory.GetPictureDatabase();

    public void Dispose()
    {
      _database.Dispose();
      _database = null;
    }
    public int AddPicture(string strPicture, int iRotation)
    {
      return _database.AddPicture(strPicture, iRotation);
    }

    public void DeletePicture(string strPicture)
    {
      _database.DeletePicture(strPicture);
    }

    public int GetRotation(string strPicture)
    {
      return _database.GetRotation(strPicture);
    }

    public void SetRotation(string strPicture, int iRotation)
    {
      _database.SetRotation(strPicture, iRotation);
    }

    public DateTime GetDateTaken(string strPicture)
    {
      return _database.GetDateTaken(strPicture);
    }

    public int EXIFOrientationToRotation(int orientation)
    {
      return _database.EXIFOrientationToRotation(orientation);
    }
  }
}
