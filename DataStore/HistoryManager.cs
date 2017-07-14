﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QuickShare.DataStore
{
    public class HistoryManager : StorageManager<HistoryRow>
    {
        internal HistoryManager(string _dbPath) : base(_dbPath, "History")
        {
        }

        public bool ContainsKey(Guid guid)
        {
            return data.Exists(x => x.Id == guid);
        }

        public void Add(Guid guid, DateTime receiveTime, string senderName, IReceivedData receivedData, bool completed)
        {
            if (ContainsKey(guid))
                Remove(guid);

            HistoryRow r = new HistoryRow()
            {
                Id = guid,
                ReceiveTime = receiveTime,
                RemoteDeviceName = senderName,
                Data = receivedData,
                Completed = completed,
            };
            data.Insert(r);
        }

        public void Remove(Guid guid)
        {
            data.Delete(x => x.Id == guid);
        }

        public HistoryRow GetItem(Guid guid)
        {
            return data.FindById(guid);
        }

        public IEnumerable<HistoryRow> GetPage(int startIndex, int count)
        {
            return data.Find(x => true, startIndex, count);
        }

        public void ChangeCompletedStatus(Guid guid, bool isCompleted)
        {
            var item = GetItem(guid);
            item.Completed = isCompleted;
            data.Update(guid, item);
        }

        public void UpdateFileName(Guid guid, string oldName, string newName, string directory)
        {
            var item = GetItem(guid);
            var d = item.Data as ReceivedFileCollection;

            if (d == null)
                return;

            var file = d.Files.FirstOrDefault(x => (x.Name == oldName && x.StorePath == directory));

            if (file == null)
                return;

            file.Name = newName;
            data.Update(guid, item);
        }
    }
}
