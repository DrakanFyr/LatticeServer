## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/tasks/stream:
    post:
      operationId: stream-tasks
      summary: Stream tasks
      description: >-
        Establishes a server streaming connection that delivers task updates in
        real-time using Server-Sent Events (SSE).


        The stream delivers all existing non-terminal tasks when first
        connected, followed by real-time

        updates for task creation and status changes. Additionally, heartbeat
        messages are sent periodically to maintain the connection.
      tags:
        - subpackage_tasks
      parameters:
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: Returns a stream of task updates as they occur.
          content:
            text/event-stream:
              schema:
                $ref: '#/components/schemas/Tasks_streamTasks_Response_200'
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
              $ref: '#/components/schemas/TaskStreamRequest'
servers:
  - url: https://example.developer.anduril.com
