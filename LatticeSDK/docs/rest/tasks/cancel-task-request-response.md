## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/tasks/{taskId}/cancel:
    put:
      operationId: cancel-task
      summary: Cancel task
      description: >-
        Cancels a task by marking it for cancellation in the system.


        This method initiates task cancellation based on the task's current
        state:

        - If the task has not been sent to an agent, it cancels immediately and
        transitions the task
          to a terminal state (`STATUS_DONE_NOT_OK` with `ERROR_CODE_CANCELLED`).
        - If the task has already been sent to an agent, the cancellation
        request is routed to the agent with a delivery status of
        `DELIVERY_STATUS_PENDING_CANCEL`.
          The agent is responsible for determining whether cancellation is possible and updating
          the task status accordingly via the `UpdateStatus` endpoint:
          - If the task can be cancelled, the agent should update the task status to `STATUS_DONE_NOT_OK`.
          - If the task cannot be cancelled, the agent should attach an error to the task stating why cancellation is not possible using `UpdateStatus`
            or the returned task object.
      tags:
        - subpackage_tasks
      parameters:
        - name: taskId
          in: path
          description: The ID of task to cancel
          required: true
          schema:
            type: string
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: Task cancellation was successful.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Task'
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
        '404':
          description: The specified resource was not found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TaskCancellation'
servers:
  - url: https://example.developer.anduril.com
