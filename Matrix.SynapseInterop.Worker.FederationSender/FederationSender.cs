﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Replication;
using Matrix.SynapseInterop.Replication.DataRows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class FederationSender
    {
        private static readonly ILogger log = Log.ForContext<FederationSender>();
        private readonly IConfiguration _config;
        private ReplicationStream<EventStreamRow> _eventStream;
        private ReplicationStream<FederationStreamRow> _fedStream;
        private int _last_ack;
        private bool _presenceEnabled;
        private int _stream_position;
        private SynapseReplication _synapseReplication;
        private TransactionQueue _transactionQueue;
        private string connectionString;
        private SigningKey key;

        public FederationSender(IConfiguration config)
        {
            _config = config;
            _last_ack = -1;
        }

        public async Task Start()
        {
            log.Information("Starting FederationWorker");
            _synapseReplication = new SynapseReplication();
            _synapseReplication.ClientName = "NetCoreFederationWorker";
            _synapseReplication.ServerName += Replication_ServerName;

            var synapseConfig = _config.GetSection("Synapse");
            key = SigningKey.ReadFromFile(synapseConfig.GetValue<string>("signingKeyPath"));
            connectionString = _config.GetConnectionString("synapse");
            _presenceEnabled = synapseConfig.GetValue("presenceEnabled", true);

            await _synapseReplication.Connect(synapseConfig.GetValue<string>("replicationHost"),
                                              synapseConfig.GetValue<int>("replicationPort"));

            _fedStream = _synapseReplication.BindStream<FederationStreamRow>();
            _fedStream.DataRow += OnFederationRow;
            _eventStream = _synapseReplication.BindStream<EventStreamRow>();
            _eventStream.PositionUpdate /**/ += OnEventPositionUpdate;
            _stream_position = await GetFederationPos("federation");
        }

        private async Task<int> GetFederationPos(string type)
        {
            using (var db = new SynapseDbContext(connectionString))
            {
                var query = db.FederationStreamPosition.Where(r => r.Type == type);
                var res = await query.FirstOrDefaultAsync();
                return res?.StreamId ?? -1;
            }
        }

        private void UpdateFederationPos(string type, int id)
        {
            using (var db = new SynapseDbContext(connectionString))
            {
                var res = db.FederationStreamPosition.SingleOrDefault(r => r.Type == type);

                if (res != null)
                {
                    res.StreamId = id;
                    db.SaveChanges();
                }
            }
        }

        private void OnFederationRow(object sender, FederationStreamRow e)
        {
            try
            {
                if (_presenceEnabled && e.presence.Count != 0) _transactionQueue.SendPresence(e.presence);

                e.edus.ForEach(_transactionQueue.SendEdu);

                foreach (var keyVal in e.keyedEdus)
                    _transactionQueue.SendEdu(keyVal.Value,
                                              keyVal.Key.Join(":"));

                e.devices.ForEach(_transactionQueue.SendDeviceMessages);
                UpdateToken(int.Parse(_fedStream.CurrentPosition));
            }
            catch (Exception ex)
            {
                log.Warning("Failed to handle transaction, got {ex}", ex);
            }
        }

        private void OnEventPositionUpdate(object sender, string stream_pos)
        {
            _transactionQueue?.OnEventUpdate(stream_pos);
        }

        private void Replication_ServerName(object sender, string serverName)
        {
            log.Information("Server name: {serverName}", serverName);

            _transactionQueue = new TransactionQueue(serverName,
                                                     connectionString,
                                                     key,
                                                     _config.GetSection("Federation"));
        }

        private void UpdateToken(int token)
        {
            _stream_position = token;

            if (_last_ack >= _stream_position) return;

            UpdateFederationPos("federation", _stream_position);
            _synapseReplication.SendFederationAck(_stream_position.ToString());
            _last_ack = token;
        }
    }
}
