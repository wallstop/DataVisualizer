#!/usr/bin/env python3
"""Pin Python bridge startup publication and Docker-route failure handling."""
from __future__ import annotations

from pathlib import Path
import socket
import sys
import threading
import unittest
from unittest.mock import Mock, patch
from urllib.error import URLError

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import unity_mcp_host as host
from unity_mcp_settings import BridgeSettings


def settings() -> BridgeSettings:
    return BridgeSettings(cliCommand='unused', cliArgs=['mcp'], projectPath='', token='', port='9020',
                timeout='100', autoSync='0', warnings=[])


class StartupTests(unittest.TestCase):
    def test_docker_route_requires_the_same_instance_before_publication(self) -> None:
        with patch.object(host.sys, 'platform', 'win32'), patch.object(host, 'log'):
            for value in ('different-listener', None, 'expected'):
                with self.subTest(value=value), patch.object(host, 'read_health', return_value={'instanceId': value}):
                    if value == 'expected':
                        host.verify_docker_host_route(9020, '0.0.0.0', 'expected')
                    else:
                        with self.assertRaises(host.DockerHostRouteError):
                            host.verify_docker_host_route(9020, '0.0.0.0', 'expected')
            for reason in (TimeoutError('silent listener'), socket.gaierror(socket.EAI_AGAIN, 'temporary DNS failure')):
                with self.subTest(reason=reason), patch.object(host, 'read_health', side_effect=URLError(reason)):
                    with self.assertRaises(host.DockerHostRouteError):
                        host.verify_docker_host_route(9020, '::', 'expected')
            with patch.object(host, 'read_health', side_effect=URLError(socket.gaierror(socket.EAI_NONAME, 'missing Docker DNS'))):
                host.verify_docker_host_route(9020, '0.0.0.0', 'expected')
            with patch.object(host, 'read_health') as read:
                host.verify_docker_host_route(9020, '127.0.0.1', 'expected')
                read.assert_not_called()

    def test_intercepted_existing_bridge_is_not_reused(self) -> None:
        with patch.object(host, 'read_health', return_value={'bridge': 'dataviz-unity-mcp', 'instanceId': 'existing'}), \
                patch.object(host, 'verify_docker_host_route', side_effect=host.DockerHostRouteError('intercepted')):
            self.assertFalse(host.existing_bridge(9020, '0.0.0.0'))

    def test_failed_local_check_never_publishes_and_closes_the_candidate(self) -> None:
        server = Mock()
        with patch.object(host, 'existing_bridge', return_value=False), patch.object(host, 'port_free', return_value=True), \
                patch.object(host, 'BridgeServer', return_value=server), patch.object(host.threading, 'Thread'), \
                patch.object(host, 'read_health', side_effect=ValueError('wrong instance')), patch.object(host, 'persist_port') as persist:
            with self.assertRaisesRegex(ValueError, 'Local HTTP self-check failed'):
                host.serve(Path('.'), settings(), 9020, '127.0.0.1', threading.Event())
            persist.assert_not_called()
            server.shutdown.assert_called_once()
            server.server_close.assert_called_once()

    def test_intercepted_candidate_is_closed_and_only_verified_port_is_published(self) -> None:
        servers: list[Mock] = []
        bridges: list[host.Bridge] = []

        def create(hostname: str, port: int, bridge: host.Bridge) -> Mock:
            server = Mock()
            servers.append(server)
            bridges.append(bridge)
            return server

        def health(port: int, hostname: str) -> host.JsonMap:
            return {'instanceId': bridges[-1].instance}

        done = threading.Event()
        done.set()
        with patch.object(host, 'existing_bridge', return_value=False), patch.object(host, 'port_free', return_value=True), \
                patch.object(host, 'BridgeServer', side_effect=create), patch.object(host.threading, 'Thread'), \
                patch.object(host, 'read_health', side_effect=health), patch.object(host, 'log'), \
                patch.object(host, 'verify_docker_host_route', side_effect=[host.DockerHostRouteError('silent'), None]), \
                patch.object(host, 'persist_port', return_value=False) as persist:
            self.assertEqual(host.serve(Path('.'), settings(), 9020, '0.0.0.0', done), 0)
            persist.assert_called_once_with(Path('.'), 9021)
        self.assertEqual(len(servers), 2)
        for server in servers:
            server.shutdown.assert_called_once()
            server.server_close.assert_called_once()

    def test_invalid_json_and_batch_notifications_keep_protocol_responses(self) -> None:
        bridge = host.Bridge(settings())
        self.addCleanup(bridge.close)
        self.assertEqual(bridge.handle(7), host.rpc_error(None, -32600, 'Invalid JSON-RPC message.'))
        self.assertIsNone(bridge.handle([]))
        response = {'jsonrpc': '2.0', 'id': 'original', 'result': {'value': '日本語'}}
        with patch.object(bridge, 'request', return_value=response), patch.object(bridge, 'write') as write:
            self.assertEqual(bridge.handle([{'id': 'original', 'method': 'test'}, {'method': 'initialized'}]), response)
            write.assert_called_once_with({'method': 'initialized'})
        with patch.object(bridge, 'write', side_effect=OSError('closed input')), patch.object(host, 'log'):
            self.assertIsNone(bridge.handle({'method': 'initialized'}))


if __name__ == '__main__':
    unittest.main()
