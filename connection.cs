using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Client;
using System.Collections.Concurrent;
using System.Net;
 
namespace SignalRClient
{
  public class Connection
  {
    private Uri _uri;
    public Uri Uri
    {
      get { return _uri; }
      set { _uri = value; }
    }
    private string _hubName;
    public string HubName
    {
      get { return _hubName; }
      set { _hubName = value; }
    }
 
    public HubConnection _hubConnection;
    public IHubProxy _commandHub;
    public ConcurrentDictionary<string, IDisposable> EventHandler = new ConcurrentDictionary<string, IDisposable>();
    public ConcurrentDictionary<string, ConcurrentQueue<string>> EventQueue = new ConcurrentDictionary<string, ConcurrentQueue<string>>();
 
    private void EnQueue(string eventName, string message)
    {
      var fReturn = false;
 
      Debug.WriteLine("{0}: EnQueue message '{1}'.", eventName, message);
      ConcurrentQueue<string> EnQueue;
      fReturn = EventQueue.TryGetValue(eventName, out EnQueue);
      if (fReturn)
      {
        EnQueue.Enqueue(message);
      }
    }
 
    public string TryDequeue(string eventName)
    {
      return Dequeue(eventName, 0);
    }
    public string Dequeue(string eventName)
    {
      return Dequeue(eventName, -1);
    }
    public string Dequeue(string eventName, int dwMillisecondsTotalWaitTime)
    {
      return Dequeue(eventName, dwMillisecondsTotalWaitTime, 100);
    }
    public string Dequeue(string eventName, int dwMillisecondsTotalWaitTime, int dwMilliSecondsWaitIntervall)
    {
      var fReturn = false;
      string message = string.Empty;
 
      IDisposable eventHandler;
      fReturn = EventHandler.TryGetValue(eventName, out eventHandler);
      if (!fReturn)
      {
        return null;
      }
      ConcurrentQueue<string> eventQueue;
      fReturn = EventQueue.TryGetValue(eventName, out eventQueue);
      if (!fReturn)
      {
        return null;
      }
      fReturn = eventQueue.TryDequeue(out message);
      if (fReturn || 0 == dwMillisecondsTotalWaitTime)
      {
        return fReturn ? message : null;
      }
      if ((dwMilliSecondsWaitIntervall > dwMillisecondsTotalWaitTime) && (-1 != dwMillisecondsTotalWaitTime))
      {
        dwMilliSecondsWaitIntervall = dwMillisecondsTotalWaitTime;
      }
      var fInfiniteWaitTime = -1 == dwMillisecondsTotalWaitTime ? true : false;
      var datNow = DateTimeOffset.UtcNow;
      do
      {
        System.Threading.Thread.Sleep(dwMilliSecondsWaitIntervall);
        fReturn = eventQueue.TryDequeue(out message);
        if (fReturn)
        {
          break;
        }
      } while (fInfiniteWaitTime || dwMillisecondsTotalWaitTime > (DateTimeOffset.UtcNow - datNow).TotalMilliseconds);
 
      return fReturn ? message : null;
    }
    public List<string> DequeueAll(string eventName)
    {
      var fReturn = false;
      var message = string.Empty;
      var messages = new List<string>();
      while(true)
      {
        message = TryDequeue(eventName);
        if (null != message)
        {
          messages.Add(message);
          continue;
        }
        break;
      }
      return messages;
    }
 
    async public Task<bool> Start(string eventName)
    {
      var fReturn = false;
 
      if (null == _commandHub)
      {
        throw new ArgumentException("_commandHub");
      }
 
      fReturn = EventHandler.TryAdd(eventName, null);
      if (!fReturn)
      {
        Debug.WriteLine(string.Format("{0}: Adding EventHandler FAILED. [{1}]", eventName, EventHandler.Count));
        return fReturn;
      }
 
      ConcurrentQueue<string> queue = null;
      fReturn = EventQueue.TryGetValue(eventName, out queue);
      if(!fReturn)
      {
        queue = new ConcurrentQueue<string>();
        fReturn = EventQueue.TryAdd(eventName, queue);
        if (!fReturn)
        {
          Debug.WriteLine(string.Format("{0}: Adding EventQueue FAILED. [{1}]", eventName, EventQueue.Count));
          return fReturn;
        }
      }
 
      IDisposable eventHandler;
      eventHandler = _commandHub.On(eventName, m =>
      {
        this.EnQueue(eventName, m);
      });
      if (null == eventHandler)
      {
        Debug.WriteLine(string.Format("{0}: Adding EventHandler FAILED. [{1}]", eventName, EventHandler.Count));
        fReturn = Stop(eventName);
        fReturn = false;
        return fReturn;
      }
      fReturn = EventHandler.TryUpdate(eventName, eventHandler, null);
      if (!fReturn)
      {
        Debug.WriteLine(string.Format("{0}: Updating EventHandler FAILED. [{1}]", eventName, EventHandler.Count));
        fReturn = this.Stop(eventName);
        fReturn = false;
        return fReturn;
      }
      Debug.WriteLine(string.Format("{0}: Starting _hubConnection ... [{1}]", eventName, EventHandler.Count));
      await _hubConnection.Start();
      fReturn = true;
      return fReturn;
    }
 
    public void Stop()
    {
      foreach (var eventHandler in EventHandler)
      {
        Stop(eventHandler.Key.ToString());
      }
    }
    public bool Stop(string eventName)
    {
      IDisposable eventHandler;
      var fReturn = false;
      fReturn = EventHandler.TryRemove(eventName, out eventHandler);
      if (!fReturn || (null == eventHandler))
      {
        return fReturn;
      }
      eventHandler.Dispose();
 
      if (0 >= EventHandler.Count)
      {
        _hubConnection.Stop();
      }
      return fReturn;
    }
 
    public Connection()
    {
      _Connection(_uri, _hubName, CredentialCache.DefaultNetworkCredentials);
    }
    public Connection(Uri uri, string hubName)
    {
      _Connection(uri, hubName, CredentialCache.DefaultNetworkCredentials);
    }
    public Connection(Uri uri, string hubName, ICredentials Credentials)
    {
      _Connection(uri, hubName, Credentials);
    }
    public Connection(Uri uri, string hubName, string Username, string Password)
    {
      var cred = new NetworkCredential(Username, Password);
      _Connection(uri, hubName, cred);
    }
    private void _Connection(Uri uri, string hubName, ICredentials Credentials)
    {
      _uri = uri;
      if (null == uri)
      {
        throw new ArgumentNullException("uri");
      }
      _hubName = hubName;
      if (string.IsNullOrWhiteSpace(hubName))
      {
        throw new ArgumentNullException("hubName");
      }
      if (null == Credentials)
      {
        throw new ArgumentNullException("Credentials");
      }
      _hubConnection = new HubConnection(_uri.AbsoluteUri);
      _hubConnection.Credentials = CredentialCache.DefaultNetworkCredentials;
      _commandHub = _hubConnection.CreateHubProxy(_hubName);
    }
 
    ~Connection()
    {
      this.Stop();
      _hubConnection.Dispose();
    }
  }
}
