#!/bin/bash

mongod --replSet rs0 --bind_ip_all &

sleep 10

mongosh --eval "rs.initiate({
  _id: 'rs0',
  members: [
    { _id: 0, host: 'mongodb:27017' }
  ]
})"

tail -f /dev/null