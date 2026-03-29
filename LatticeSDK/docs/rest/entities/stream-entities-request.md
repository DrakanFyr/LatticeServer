## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/entities/stream:
    post:
      operationId: stream-entities
      summary: Stream entity events
      description: >-
        Establishes a server-sent events (SSE) connection that streams entity
        data in real-time.

        This is a one-way connection from server to client that follows the SSE
        protocol with text/event-stream content type.


        This endpoint enables clients to maintain a real-time view of the common
        operational picture (COP)

        by first streaming all pre-existing entities that match filter criteria,
        then continuously delivering

        updates as entities are created, modified, or deleted.


        The server first sends events with type PREEXISTING for all live
        entities matching the filter that existed before the stream was open,

        then streams CREATE events for newly created entities, UPDATE events
        when existing entities change, and DELETED events when entities are
        removed. The stream remains open

        indefinitely unless preExistingOnly is set to true.


        Heartbeat messages can be configured to maintain connection health and
        detect disconnects by setting the heartbeatIntervalMS

        parameter. These heartbeats help keep the connection alive and allow
        clients to verify the server is still responsive.


        Clients can optimize bandwidth usage by specifying which entity
        components they need populated using the componentsToInclude parameter.

        This allows receiving only relevant data instead of complete entities.


        The connection automatically recovers from temporary disconnections,
        resuming the stream where it left off. Unlike polling approaches,

        this provides real-time updates with minimal latency and reduced server
        load.
      tags:
        - subpackage_entities
      parameters:
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: >-
            Returns all pre-existing data and then return all new data as they
            become available.
          content:
            text/event-stream:
              schema:
                $ref: '#/components/schemas/Entities_streamEntities_Response_200'
        '400':
          description: Bad request
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
        '401':
          description: Unauthorized to access resource
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/EntityStreamRequest'
servers:
  - url: https://example.developer.anduril.com
