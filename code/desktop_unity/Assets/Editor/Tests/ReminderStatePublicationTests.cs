using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ReminderStatePublicationTests
{
    private GameObject _host;
    private ReminderManager _manager;
    private string _path;
    private int _added, _done, _changed;

    [SetUp]
    public void SetUp()
    {
        _added = _done = _changed = 0;
        _host = new GameObject("reminder-state-publication-test");
        _host.SetActive(false);
        _manager = _host.AddComponent<ReminderManager>();
        _path = Path.Combine(Path.GetTempPath(), "fuxuan-reminder-" + Guid.NewGuid().ToString("N") + ".json");
        typeof(ReminderManager).GetField("_filePath", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_manager, _path);
        _manager.OnReminderAdded += _ => _added++;
        _manager.OnReminderDone += _ => _done++;
        _manager.OnDataChanged += reminders =>
        {
            _changed++;
            var saved = JsonUtility.FromJson<ReminderManager.ReminderList>(File.ReadAllText(_path));
            Assert.AreEqual(reminders.Count, saved.reminders.Count);
            Assert.AreEqual(_manager.PendingCount, saved.reminders.FindAll(r => !r.done).Count);
        };
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(_host);
        if (File.Exists(_path)) File.Delete(_path);
    }

    private void Invoke(string method, params object[] args)
    {
        typeof(ReminderManager).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_manager, args);
    }

    [Test]
    public void CrudPublishesOncePerSuccessfulMutationAfterSaving()
    {
        var reminder = _manager.AddReminder("isolated", DateTime.Now.AddDays(1));
        Assert.AreEqual(1, _added);
        Assert.AreEqual(1, _changed);
        Assert.IsTrue(_manager.MarkDone(reminder.id));
        Assert.AreEqual(1, _done);
        Assert.AreEqual(2, _changed);
        Assert.IsTrue(_manager.DeleteReminder(reminder.id));
        Assert.AreEqual(3, _changed);
        Assert.IsFalse(_manager.DeleteReminder(reminder.id));
        Assert.AreEqual(3, _changed);
    }

    [Test]
    public void MissedRemindersPublishOneSavedBatchWithoutDelivery()
    {
        _manager.AddReminder("missed-one", DateTime.Now.AddHours(-1));
        _manager.AddReminder("missed-two", DateTime.Now.AddHours(-2));
        _changed = 0;
        Invoke("CheckDueReminders");
        Assert.AreEqual(2, _done);
        Assert.AreEqual(1, _changed);
        Assert.AreEqual(0, _manager.PendingCount);
        Invoke("CheckDueReminders");
        Assert.AreEqual(1, _changed);
    }

    [TestCase(null)]
    [TestCase("daily")]
    [TestCase("weekday")]
    [TestCase("weekly")]
    public void CompletingOccurrencePublishesSavedStateWithoutSendingNotifications(string recurring)
    {
        DateTime due = DateTime.Now;
        var reminder = _manager.AddReminder("occurrence", due, recurring);
        _changed = 0;
        // Deliberately bypass TriggerReminder: no Toast, phone, tool or model calls.
        Invoke("CompleteReminderOccurrence", reminder);
        Assert.AreEqual(1, _changed);
        Assert.AreEqual(recurring == null ? 1 : 0, _done);
        Assert.AreEqual(recurring == null, reminder.done);
        if (recurring != null) Assert.Greater(DateTime.Parse(reminder.remindAt), due);
        var saved = JsonUtility.FromJson<ReminderManager.ReminderList>(File.ReadAllText(_path));
        Assert.AreEqual(reminder.remindAt, saved.reminders[0].remindAt);
    }
}
